using System.Collections.ObjectModel;
using NexInstaller.Core.Importing;

namespace NexInstaller.App.ViewModels;

public sealed class FolderToolsViewModel : ObservableObject
{
    private string newFolderName = "";
    private CategoryOption? destination;
    private bool busy;
    private bool creating;
    public bool IsCreating { get => creating; private set { creating = value; Raise(); } }
    public RelayCommand NewFolderCommand { get; }
    public RelayCommand CancelCreateCommand { get; }
    public ObservableCollection<FolderNavigationViewModel> BrowserFolders { get; } = [];
    private readonly ShellViewModel shell;
    private readonly LibrarySection section;
    private readonly Func<Task> create;
    private readonly Func<Task> move;
    private readonly Func<string, Task> delete;
    public Task CreateAsync() => create();
    public Task MoveAsync() => move();
    public Task DeleteAsync(string name) => delete(name);
    public string NewFolderName { get => newFolderName; set { newFolderName = value; Raise(); } }
    public CategoryOption? Destination { get => destination; set { destination = value; Raise(); } }
    public bool IsIdle => !busy;
    public ObservableCollection<CategoryOption> Destinations { get; } = [];
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand MoveCommand { get; }

    public FolderToolsViewModel(ShellViewModel shell, LibrarySection section, Func<IReadOnlyList<string>> selectedIds, Action<string> report)
    {
        this.shell = shell; this.section = section;
        ReportDeleteError = report;
        NewFolderCommand = new(() => { NewFolderName = ""; IsCreating = true; });
        CancelCreateCommand = new(() => { IsCreating = false; NewFolderName = ""; });
        create = () => Run(async service =>
        {
            var name = LibraryFolder.Normalize(NewFolderName);
            if (Destinations.Any(option => string.Equals(option.Value, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Folder already exists.");
            await service.CreateAsync(section, name);
            await shell.RefreshFolderSectionAsync(section);
            Destination = Destinations.FirstOrDefault(option => string.Equals(option.Value, name, StringComparison.OrdinalIgnoreCase));
            IsCreating = false;
            NewFolderName = ""; report($"Folder '{name}' created. Select files and choose Move, or open the folder below.");
        });
        move = () => Run(async service =>
        {
            var target = Destination?.Value ?? throw new InvalidOperationException("Choose a destination folder.");
            var ids = selectedIds();
            await service.AssignAsync(section, ids, target);
            await shell.RefreshFolderSectionAsync(section);
            shell.SelectLibraryFolder(section, target);
            report($"Moved {ids.Count} item(s) to {(target.Length == 0 ? "Unfiled" : target)}.");
        });
        CreateCommand = new(CreateAsync, ex => report(ex.Message));
        MoveCommand = new(MoveAsync, ex => report(ex.Message));
        delete = name => Run(async service =>
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Choose a named folder to delete.");
            if (!shell.ConfirmFolderDeletion(name)) { report("Folder deletion cancelled."); return; }
            await service.DeleteAsync(section, name);
            await shell.RefreshFolderSectionAsync(section);
            shell.ClearDeletedFolderDestination(section, name);
            report($"Folder '{name}' deleted. Its files are kept in Unfiled.");
        });

        async Task Run(Func<ILibraryFolderService, Task> operation)
        {
            if (busy) return;
            shell.CheckLibraryManagementAvailable();
            var service = shell.LibraryFolders ?? throw new InvalidOperationException("Folder management is unavailable.");
            busy = true; Raise(nameof(IsIdle)); shell.InstallationQueue.IsLibraryChanging = true;
            try { await operation(service); }
            finally { busy = false; Raise(nameof(IsIdle)); shell.InstallationQueue.IsLibraryChanging = false; }
        }
    }

    public void Load(IEnumerable<string> names)
    {
        var snapshot = names.Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var previous = Destination?.Value;
        Destinations.Clear(); Destinations.Add(new("Unfiled", ""));
        foreach (var name in snapshot) Destinations.Add(new(name, name));
        Destination = Destinations.FirstOrDefault(option => option.Value == previous) ?? Destinations[0];
        BrowserFolders.Clear();
        BrowserFolders.Add(new("All files", () => shell.SelectLibraryFolder(section, null)));
        BrowserFolders.Add(new("Unfiled", () => shell.SelectLibraryFolder(section, "")));
        foreach (var name in snapshot) BrowserFolders.Add(new(name, () => shell.SelectLibraryFolder(section, name), new(() => DeleteAsync(name), ex => ReportDeleteError(ex.Message))));
    }

    private Action<string> ReportDeleteError { get; set; } = _ => { };
}

public sealed class FolderNavigationViewModel(string name, Action open, AsyncCommand? delete = null)
{
    public string Name { get; } = name;
    public RelayCommand OpenCommand { get; } = new(open);
    public AsyncCommand? DeleteCommand { get; } = delete;
    public bool CanDelete => DeleteCommand is not null;
}
