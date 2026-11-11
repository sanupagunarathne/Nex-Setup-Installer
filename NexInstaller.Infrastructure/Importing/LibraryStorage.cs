using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Infrastructure.Importing;

public sealed class LibraryStorage(LibraryPaths paths)
{
    public LibraryPaths Paths { get; } = paths;
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static void CheckPath(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked/reparse-point paths are not permitted: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public FileStream BeginWrite()
    {
        CheckPath(Paths.Root);
        Directory.CreateDirectory(Paths.Root);
        CheckPath(Path.Combine(Paths.Root, ".nex-library.lock"));
        return new FileStream(Path.Combine(Paths.Root, ".nex-library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void CreateDirectory(string path) { CheckPath(path); Directory.CreateDirectory(path); CheckPath(path); }
    public IReadOnlyList<(string Path, PackageManifest Manifest)> ReadManifests()
    {
        CheckPath(Paths.Manifests);
        if (!Directory.Exists(Paths.Manifests)) return [];
        var list = new List<(string, PackageManifest)>();
        foreach (var path in Directory.EnumerateFiles(Paths.Manifests).Where(p => Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            CheckPath(path);
            if (new FileInfo(path).Length > JsonPackageManifestReader.MaximumBytes) throw new InvalidDataException("An existing manifest is too large. Repair it before modifying the library.");
            var result = new JsonPackageManifestReader().Parse(File.ReadAllText(path));
            if (!result.IsValid) throw new InvalidDataException($"Repair {Path.GetFileName(path)} before modifying the library: {string.Join("; ", result.Errors)}");
            list.Add((path, result.Manifest!));
        }
        return list;
    }
    public static string Hash(string path, CancellationToken token)
    {
        CheckPath(path);
        using var file = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = file.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static IReadOnlyList<string> Files(string directory)
    {
        CheckPath(directory);
        var result = new List<string>();
        Walk(directory);
        return result;
        void Walk(string folder)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                CheckPath(path);
                if (Directory.Exists(path)) Walk(path); else result.Add(path);
                if (result.Count > 10000) throw new InvalidDataException("A single import is limited to 10000 files.");
            }
        }
    }
    public static bool IsWithin(string candidate, string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        return full.Equals(Path.TrimEndingDirectorySeparator(prefix), StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
