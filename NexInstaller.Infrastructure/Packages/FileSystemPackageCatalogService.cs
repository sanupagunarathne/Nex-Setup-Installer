using System.Text;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Packages;

namespace NexInstaller.Infrastructure.Packages;

public sealed class FileSystemPackageCatalogService(LibraryPaths paths, IPackageManifestReader reader, PackageArchitecture hostArchitecture) : IPackageCatalogService
{
    public Task<PackageCatalog> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Discover(cancellationToken), cancellationToken);

    private PackageCatalog Discover(CancellationToken token)
    {
        var packages = new List<CatalogPackage>();
        var issues = new List<CatalogIssue>();
        try
        {
            EnsureNoLinks(paths.Root, "manifests");
            foreach (var file in Directory.EnumerateFiles(paths.Manifests).Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (!Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = "manifests/" + Path.GetFileName(file);
                try
                {
                    EnsureNoLinks(paths.Root, relative);
                    using var input = File.OpenRead(file);
                    if (input.Length > JsonPackageManifestReader.MaximumBytes) throw new IOException("Manifest exceeds the 1 MiB limit.");
                    var bytes = new byte[JsonPackageManifestReader.MaximumBytes + 1];
                    var count = 0;
                    while (count < bytes.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        var read = input.Read(bytes, count, bytes.Length - count);
                        if (read == 0) break;
                        count += read;
                    }
                    if (count > JsonPackageManifestReader.MaximumBytes) throw new IOException("Manifest exceeds the 1 MiB limit.");
                    var json = new UTF8Encoding(false, true).GetString(bytes, 0, count).TrimStart('\uFEFF');
                    var result = reader.Parse(json);
                    if (!result.IsValid)
                    {
                        foreach (var error in result.Errors) issues.Add(new(relative, error));
                        continue;
                    }
                    var manifest = result.Manifest!;
                    var problems = new List<string>();
                    long size = 0;
                    if (!ArchitectureCompatibility.IsCompatible(manifest.Architecture, hostArchitecture))
                        problems.Add($"Architecture {manifest.Architecture} is incompatible with this {hostArchitecture} workstation.");
                    foreach (var payload in manifest.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            EnsureNoLinks(paths.Root, PortablePackagePath.Normalize(payload.Path));
                            var resolved = PortablePackagePath.Resolve(paths.Root, payload.Path);
                            using var content = File.Open(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
                            size = checked(size + content.Length);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
                        {
                            problems.Add($"Missing, unreadable, or unsafe file: {payload.Path}. {ex.Message}");
                        }
                    }
                    packages.Add(new(manifest, relative, size, problems.AsReadOnly()));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException)
                {
                    issues.Add(new(relative, $"Cannot read manifest: {ex.Message}"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            issues.Add(new("manifests", $"Cannot fully scan manifest folder: {ex.Message}"));
        }
        var duplicates = packages.GroupBy(p => p.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var final = packages.Select(p => duplicates.Contains(p.Manifest.Id)
            ? p with { Problems = p.Problems.Concat(["Duplicate package ID. All manifests with this ID are unavailable."]).ToArray() } : p)
            .OrderBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Manifest.Id, StringComparer.Ordinal).ToArray();
        return new(final, issues.AsReadOnly());
    }

    private static void EnsureNoLinks(string root, string relative)
    {
        var current = Path.GetFullPath(root);
        RejectLink(current);
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked or reparse-point paths are not allowed.");
    }
}
