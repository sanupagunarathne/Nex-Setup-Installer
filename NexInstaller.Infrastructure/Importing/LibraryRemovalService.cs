using NexInstaller.Core.Importing;
using NexInstaller.Infrastructure.Packages;

namespace NexInstaller.Infrastructure.Importing;

/// <summary>Removes saved files from discovery while retaining a recoverable copy in .trash.</summary>
public sealed class LibraryRemovalService(LibraryStorage storage) : ILibraryRemovalService
{
    public Task RemovePackagesAsync(IReadOnlyList<string> packageIds, CancellationToken token = default) => Task.Run(() =>
    {
        using var gate = storage.BeginWrite();
        var ids = packageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifests = storage.ReadManifests();
        var selected = manifests.Where(item => ids.Contains(item.Manifest.Id)).ToArray();
        if (selected.Length != ids.Count) throw new InvalidDataException("Some apps are missing or have duplicate IDs. Refresh the library before deleting.");
        var retained = manifests.Where(item => !ids.Contains(item.Manifest.Id)).SelectMany(item => item.Manifest.Files)
            .Select(file => PortablePackagePath.Resolve(storage.Paths.Root, file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = selected.SelectMany(item => item.Manifest.Files).Select(file => PortablePackagePath.Resolve(storage.Paths.Root, file.Path))
            .Where(path => !retained.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        files.AddRange(selected.Select(item => item.Path));
        MoveToTrash(files, token);
    }, token);

    public Task RemoveFontAsync(string path, CancellationToken token = default) => Task.Run(() =>
    {
        using var gate = storage.BeginWrite();
        var full = Path.GetFullPath(path);
        if (!LibraryStorage.IsWithin(full, storage.Paths.Fonts) || Path.GetExtension(full).ToLowerInvariant() is not (".ttf" or ".otf"))
            throw new InvalidDataException("Select a font saved inside this library.");
        MoveToTrash([full], token);
    }, token);

    private void MoveToTrash(IReadOnlyList<string> paths, CancellationToken token)
    {
        var trash = Path.Combine(storage.Paths.Root, ".trash", Guid.NewGuid().ToString("N"));
        var moves = new List<(string Source, string Target)>();
        // Validate every source and destination before moving anything.
        foreach (var source in paths)
        {
            var full = Path.GetFullPath(source);
            if (!LibraryStorage.IsWithin(full, storage.Paths.Packages) && !LibraryStorage.IsWithin(full, storage.Paths.Manifests) && !LibraryStorage.IsWithin(full, storage.Paths.Fonts))
                throw new InvalidDataException("Deletion is limited to saved app manifests, installers, and fonts.");
            LibraryStorage.CheckPath(full);
            if (!File.Exists(full)) continue;
            var target = Path.GetFullPath(Path.Combine(trash, Path.GetRelativePath(storage.Paths.Root, full)));
            if (!LibraryStorage.IsWithin(target, trash)) throw new InvalidDataException("Unsafe trash destination.");
            LibraryStorage.CheckPath(target); moves.Add((full, target));
        }
        var completed = new List<(string Source, string Target)>();
        try
        {
            foreach (var move in moves)
            {
                token.ThrowIfCancellationRequested();
                storage.CreateDirectory(Path.GetDirectoryName(move.Target)!);
                LibraryStorage.CheckPath(move.Source);
                File.Move(move.Source, move.Target);
                completed.Add(move);
            }
        }
        catch
        {
            foreach (var move in completed.AsEnumerable().Reverse()) File.Move(move.Target, move.Source);
            throw;
        }
    }
}
