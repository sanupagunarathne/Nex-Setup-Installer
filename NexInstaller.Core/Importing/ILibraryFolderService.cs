namespace NexInstaller.Core.Importing;

public enum LibrarySection { Apps, Fonts, Presets }

// Folder membership is metadata so organizing a library never breaks saved paths or queues.
public sealed record LibraryFolderInventory(IReadOnlyList<string> Folders, IReadOnlyDictionary<string, string> Assignments)
{
    public string Resolve(string id, string original = "") => Assignments.TryGetValue(id, out var folder) ? folder : original;
    public static LibraryFolderInventory Empty { get; } = new([], new Dictionary<string, string>());
}

public interface ILibraryFolderService
{
    Task<LibraryFolderInventory> ReadAsync(LibrarySection section, CancellationToken token = default);
    Task CreateAsync(LibrarySection section, string name, CancellationToken token = default);
    Task AssignAsync(LibrarySection section, IReadOnlyList<string> ids, string folder, CancellationToken token = default);
    Task DeleteAsync(LibrarySection section, string folder, CancellationToken token = default);
}
