using System.Collections.ObjectModel;
using System.IO;
namespace NexInstaller.App.ViewModels;

public sealed record ImportGroupChoice(string Label, string? Value, bool IsCreate = false);
public sealed partial class PackageManagerViewModel
{
    private int step;
    private bool creatingGroup;
    private ImportGroupChoice? selectedImportGroup;
    public int Step { get => step; private set { step = value; Raise(); Raise(nameof(IsChooseStep)); Raise(nameof(IsDetailsStep)); Raise(nameof(IsReviewStep)); } }
    public bool IsChooseStep => Step == 0;
    public bool IsDetailsStep => Step == 1;
    public bool IsReviewStep => Step == 2;
    public bool IsFolderSource => Directory.Exists(SourcePath);
    public string FileDisplayName => string.IsNullOrWhiteSpace(SourcePath) ? "No file chosen" : Path.GetFileName(SourcePath.TrimEnd('/', '\\'));
    public bool IsCreatingGroup { get => creatingGroup; private set { creatingGroup = value; Raise(); } }
    public ObservableCollection<ImportGroupChoice> ImportGroups { get; } = [];
    public ImportGroupChoice? SelectedImportGroup
    {
        get => selectedImportGroup;
        set
        {
            if (value is null || IsBusy || ReferenceEquals(value, selectedImportGroup)) return;
            if (value.IsCreate) { selectedImportGroup = value; IsCreatingGroup = true; Raise(); return; }
            selectedImportGroup = value; IsCreatingGroup = false; Category = value.Value!; Raise();
        }
    }
    public RelayCommand NextCommand => new(() =>
    {
        if (IsBusy) return;
        try
        {
            if (Step == 0)
            {
                if (IsFolderSource)
                {
                    var entry = EntryPoint.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(entry) || Path.IsPathRooted(entry) || entry.Split('/').Contains("..") || !File.Exists(Path.Combine(SourcePath, EntryPoint)))
                        throw new InvalidOperationException("Choose the setup file inside this folder before continuing.");
                    if (Path.GetExtension(entry).ToLowerInvariant() is not (".exe" or ".msi")) throw new InvalidOperationException("The setup file must end in .exe or .msi.");
                }
                else if (!File.Exists(SourcePath) || Path.GetExtension(SourcePath).ToLowerInvariant() is not (".exe" or ".msi"))
                    throw new InvalidOperationException("Choose an existing setup file ending in .exe or .msi, or choose its complete folder.");
                Step = 1; Status = "Add the app details. More options keeps the technical settings available.";
            }
            else if (Step == 1)
            {
                if (IsCreatingGroup) throw new InvalidOperationException("Create the new group or cancel it before reviewing this app.");
                if (string.IsNullOrWhiteSpace(PackageName) || string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(Category))
                    throw new InvalidOperationException("Enter the app name and version, and choose a group before reviewing.");
                Step = 2; Status = "Check the details below. Saving only copies files; it does not install anything.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException) { Status = ex.Message; }
    });
    public RelayCommand BackCommand => new(() => { if (!IsBusy && Step > 0) Step--; });
    public RelayCommand CancelCreateGroupCommand => new(() => { IsCreatingGroup = false; SelectCurrentGroup(); });
    public AsyncCommand CreateImportGroupCommand => new(CreateImportGroupAsync, ex => Status = ex.Message);
    public string ReviewText => $"App: {PackageName}\nVersion: {Version}\nGroup: {Category}\nComputer type: {Architecture}\nWindows administrator permission: {(RequiresAdministrator ? "Required" : "Not declared as required")}\nSetup mode: Normal setup windows (manual)\n\nSource: {SourcePath}\n" +
        (IsFolderSource ? $"Setup file inside folder: {EntryPoint}\nAll files in this folder will be copied.\n" : "") +
        (string.IsNullOrWhiteSpace(Description) ? "" : $"\nNote: {Description}\n") + "\nFiles will be copied and checked for changes. Nothing is installed by saving.";
    private void RebuildGroups(IEnumerable<string> existing)
    {
        var names = new[] { "Browsers", "Office", "Media", "Security", "Design & editing", "Tools", "Development" }
            .Concat(existing).Append(Category).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ImportGroups.Clear(); foreach (var name in names) ImportGroups.Add(new(name, name));
        ImportGroups.Add(new("+ Create new group…", null, true)); SelectCurrentGroup();
    }
    private void SelectCurrentGroup()
    {
        var match = ImportGroups.FirstOrDefault(option => !option.IsCreate && string.Equals(option.Value, Category, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            match = new(Category, Category); ImportGroups.Insert(Math.Max(0, ImportGroups.Count - 1), match);
        }
        selectedImportGroup = match; Raise(nameof(SelectedImportGroup));
    }
    public Task CreateImportGroupAsync() => Work(async token =>
    {
        var name = NewCategory.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a group name before creating it.");
        var existing = ImportGroups.FirstOrDefault(option => !option.IsCreate && string.Equals(option.Value, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            await Task.Run(() => { token.ThrowIfCancellationRequested(); Require(categories).Add(name); }, token);
            await shell.RefreshAsync();
        }
        Category = existing?.Value ?? name; IsCreatingGroup = false; NewCategory = "";
        Status = "Group selected: " + Category;
    });
}
