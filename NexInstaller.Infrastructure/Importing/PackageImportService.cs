using System.Text.Json;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Infrastructure.Importing;

public sealed class PackageImportService(LibraryStorage storage) : IPackageImportService
{
    public Task<PackageImportResult> ImportAsync(PackageImportRequest request, IProgress<string>? progress = null, CancellationToken token = default) =>
        Task.Run(() => Import(request, progress, token), token);

    private PackageImportResult Import(PackageImportRequest request, IProgress<string>? progress, CancellationToken token)
    {
        var source = Path.GetFullPath(request.SourcePath);
        LibraryStorage.CheckPath(source);
        var folder = Directory.Exists(source);
        if (!folder && !File.Exists(source)) throw new FileNotFoundException("Select an existing EXE, MSI, or installation folder.");
        if (LibraryStorage.IsWithin(source, storage.Paths.Root) || (folder && LibraryStorage.IsWithin(storage.Paths.Root, source)))
            throw new InvalidDataException("Import from a location outside the library, which must not be inside the source folder.");
        var sourceRoot = folder ? source : Path.GetDirectoryName(source)!;
        var files = folder ? LibraryStorage.Files(source) : [source];
        var entrySuffix = folder ? request.EntryPoint?.Replace('\\', '/') : Path.GetFileName(source);
        var id = "pkg-" + Guid.NewGuid().ToString("N");
        var libraryFolder = LibraryFolder.Normalize(request.Folder);
        var packageDirectory = libraryFolder.Length == 0 ? id : $"folders/{libraryFolder}/{id}";
        var entry = PortablePackagePath.Normalize($"packages/{packageDirectory}/{entrySuffix}");
        var extension = Path.GetExtension(entry).ToLowerInvariant();
        if (extension is not (".exe" or ".msi")) throw new InvalidDataException("Select an EXE/MSI entry point inside the source folder.");
        var records = new List<PackageFile>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            progress?.Report("Hashing " + relative);
            records.Add(new() { Path = PortablePackagePath.Normalize($"packages/{packageDirectory}/{relative}"), Sha256 = LibraryStorage.Hash(file, token) });
        }
        var manifest = new PackageManifest { SchemaVersion = 1, Id = id, Name = request.Name, Version = request.Version,
            Architecture = request.Architecture, Category = request.Category, Description = request.Description, Folder = libraryFolder.Length == 0 ? null : libraryFolder,
            InstallerType = extension == ".msi" ? InstallerType.Msi : InstallerType.Exe, EntryPoint = entry,
            Files = records.ToArray(), InstallationMode = InstallationMode.Manual, RequiresAdministrator = request.RequiresAdministrator };
        var json = JsonSerializer.Serialize(manifest, LibraryStorage.Json);
        var validation = new JsonPackageManifestReader().Parse(json);
        if (!validation.IsValid) throw new InvalidDataException(string.Join("\n", validation.Errors));
        using var gate = storage.BeginWrite();
        var existing = storage.ReadManifests();
        var fingerprint = Fingerprint(manifest);
        var duplicate = existing.FirstOrDefault(item => Fingerprint(item.Manifest) == fingerprint);
        if (duplicate.Manifest is not null) return new(false, "Duplicate content: this package is already in the library.", duplicate.Manifest);
        var staging = Path.Combine(storage.Paths.Root, ".staging", id);
        var destination = Path.Combine(storage.Paths.Packages, packageDirectory);
        var committed = false;
        var moved = false;
        try
        {
            storage.CreateDirectory(staging);
            for (var index = 0; index < files.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourceRoot, files[index]);
                var target = Path.Combine(staging, relative);
                storage.CreateDirectory(Path.GetDirectoryName(target)!);
                LibraryStorage.CheckPath(files[index]);
                progress?.Report($"Copying {index + 1}/{files.Count}: {relative}");
                using (var input = File.OpenRead(files[index]))
                using (var output = new FileStream(target, FileMode.CreateNew))
                {
                    var buffer = new byte[1024 * 1024];
                    int read;
                    while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                    output.Flush(true);
                }
                if (!LibraryStorage.Hash(target, token).Equals(records[index].Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Source changed during import. No package was committed.");
            }
            storage.CreateDirectory(Path.GetDirectoryName(destination)!);
            storage.CreateDirectory(storage.Paths.Manifests);
            var stagedManifest = Path.Combine(storage.Paths.Manifests, id + ".tmp");
            try
            {
                using (var writer = new StreamWriter(new FileStream(stagedManifest, FileMode.CreateNew))) writer.Write(json);
                token.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
                moved = true;
                File.Move(stagedManifest, Path.Combine(storage.Paths.Manifests, id + ".json"));
                committed = true;
            }
            finally { if (File.Exists(stagedManifest)) File.Delete(stagedManifest); }
            progress?.Report("Import complete. No installer was executed.");
            return new(true, "Package imported in manual mode. Copied files passed SHA-256 verification.", manifest);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (!committed && moved && Directory.Exists(destination)) Directory.Delete(destination, true);
        }
    }

    private static string Fingerprint(PackageManifest manifest) =>
        manifest.Files.First(file => file.Path.Replace('\\', '/').Equals(manifest.EntryPoint.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).Sha256.ToUpperInvariant() + ":" +
        string.Join("|", manifest.Files.Select(f => f.Sha256.ToUpperInvariant()).Order(StringComparer.Ordinal));

    public Task<IReadOnlyList<VerificationResult>> VerifyAsync(IProgress<string>? progress = null, CancellationToken token = default) => Task.Run<IReadOnlyList<VerificationResult>>(() =>
    {
        var results = new List<VerificationResult>();
        foreach (var (_, manifest) in storage.ReadManifests())
        {
            var problems = new List<string>();
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report("Verifying " + file.Path);
                try
                {
                    var resolved = PortablePackagePath.Resolve(storage.Paths.Root, file.Path);
                    if (!LibraryStorage.Hash(resolved, token).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) problems.Add("SHA-256 mismatch: " + file.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add(file.Path + ": " + ex.Message); }
            }
            results.Add(new(manifest.Id, problems));
        }
        return results;
    }, token);
}
