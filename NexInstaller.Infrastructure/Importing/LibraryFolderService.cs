using System.Text.Json;
using System.Text.Json.Serialization;
using NexInstaller.Core.Importing;

namespace NexInstaller.Infrastructure.Importing;

public sealed class LibraryFolderService(LibraryStorage storage) : ILibraryFolderService
{
    private sealed record FolderData(string[] Folders, Dictionary<string, string> Assignments);
    private static readonly JsonSerializerOptions Json = new(LibraryStorage.Json)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };
    private string PathFor(LibrarySection section)
    {
        if (!Enum.IsDefined(section)) throw new ArgumentException("Unknown library section.");
        return Path.Combine(storage.Paths.Root, "library-folders-" + section.ToString().ToLowerInvariant() + ".json");
    }
    private FolderData Read(LibrarySection section)
    {
        var path = PathFor(section);
        LibraryStorage.CheckPath(path);
        if (!File.Exists(path)) return new([], new(StringComparer.OrdinalIgnoreCase));
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Folder configuration exceeds 2 MiB.");
        var data = JsonSerializer.Deserialize<FolderData>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Invalid folder configuration.");
        if (data.Folders is null || data.Assignments is null || data.Folders.Length > 1000 || data.Assignments.Count > 10000)
            throw new InvalidDataException("Invalid folder configuration or folder limit exceeded.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in data.Folders)
            if (string.IsNullOrEmpty(name) || LibraryFolder.Normalize(name) != name || !names.Add(name))
                throw new InvalidDataException("Invalid or duplicate saved folder name.");
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, folder) in data.Assignments)
        {
            ValidateId(id);
            if (folder is null || (folder.Length > 0 && !names.Contains(folder)) || !assignments.TryAdd(id, folder))
                throw new InvalidDataException("Invalid saved folder membership.");
        }
        return data with { Assignments = assignments };
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || id.Any(char.IsControl))
            throw new ArgumentException("Choose a saved library item first.");
    }
    public Task<LibraryFolderInventory> ReadAsync(LibrarySection section, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); var data = Read(section);
        return new LibraryFolderInventory(data.Folders, data.Assignments);
    }, token);
    public Task CreateAsync(LibrarySection section, string name, CancellationToken token = default) => Change(section, data =>
    {
        var normalized = LibraryFolder.Normalize(name);
        if (normalized.Length == 0) throw new ArgumentException("Enter a folder name.");
        if (data.Folders.Contains(normalized, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Folder already exists.");
        return data with { Folders = [.. data.Folders, normalized] };
    }, token);
    public Task AssignAsync(LibrarySection section, IReadOnlyList<string> ids, string folder, CancellationToken token = default)
    {
        var snapshot = ids.ToArray();
        return Change(section, data =>
        {
            if (snapshot.Length == 0) throw new InvalidOperationException("Select an item to move first.");
            var normalized = LibraryFolder.Normalize(folder);
            var canonical = data.Folders.FirstOrDefault(name => name.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? normalized;
            foreach (var id in snapshot) { ValidateId(id); data.Assignments[id] = canonical; }
            return canonical.Length > 0 && !data.Folders.Contains(canonical, StringComparer.OrdinalIgnoreCase)
                ? data with { Folders = [.. data.Folders, canonical] } : data;
        }, token);
    }
    public Task DeleteAsync(LibrarySection section, string folder, CancellationToken token = default) => Change(section, data =>
    {
        var name = LibraryFolder.Normalize(folder);
        if (name.Length == 0) throw new ArgumentException("Choose a named folder to delete.");
        var original = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (section == LibrarySection.Apps)
            foreach (var item in storage.ReadManifests()) original.Add(item.Manifest.Id, item.Manifest.Folder ?? "");
        else if (section == LibrarySection.Fonts && Directory.Exists(storage.Paths.Fonts))
            foreach (var path in LibraryStorage.Files(storage.Paths.Fonts).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf"))
            {
                var relative = Path.GetRelativePath(storage.Paths.Fonts, path).Replace('\\', '/');
                var parts = relative.Split('/');
                original.Add("fonts/" + relative, parts.Length >= 4 && parts[0] == "folders" ? parts[1] : "");
            }
        if (!data.Folders.Concat(original.Values).Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Folder no longer exists. Refresh the library.");
        // Empty assignments mask the original import folders without moving payloads or breaking references.
        foreach (var (id, initialFolder) in original)
            if ((data.Assignments.TryGetValue(id, out var current) ? current : initialFolder).Equals(name, StringComparison.OrdinalIgnoreCase))
                data.Assignments[id] = "";
        foreach (var id in data.Assignments.Where(item => item.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(item => item.Key).ToArray())
            data.Assignments[id] = "";
        return data with { Folders = data.Folders.Where(value => !value.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray() };
    }, token);

    private Task Change(LibrarySection section, Func<FolderData, FolderData> change, CancellationToken token) => Task.Run(() =>
    {
        using var gate = storage.BeginWrite();
        var data = change(Read(section));
        if (data.Folders.Length > 1000 || data.Assignments.Count > 10000) throw new InvalidOperationException("Folder configuration limit reached.");
        var json = JsonSerializer.Serialize(data, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) throw new InvalidOperationException("Folder configuration exceeds 2 MiB.");
        var target = PathFor(section); LibraryStorage.CheckPath(target);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew))) { writer.Write(json); writer.Flush(); ((FileStream)writer.BaseStream).Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);
}
