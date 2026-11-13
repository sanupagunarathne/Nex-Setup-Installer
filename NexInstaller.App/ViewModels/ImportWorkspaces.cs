using System.Collections.ObjectModel;
using System.IO;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Packages;
namespace NexInstaller.App.ViewModels;

public interface IImportDialogs
{
    string? Installer();
    IReadOnlyList<string> Installers() { var path = Installer(); return path is null ? [] : [path]; }
    string? Folder();
    string? EntryPoint(string directory);
    IReadOnlyList<string> Fonts();
    bool Confirm(string message);
}

public abstract class ImportWorkspace(string title, string subtitle) : PageViewModel(title, subtitle)
{
    private CancellationTokenSource? pending;
    private bool busy;
    private string status = "Ready.";
    public bool IsBusy { get => busy; private set { busy = value; Raise(); Raise(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public string Status { get => status; protected set { status = value; Raise(); } }
    public RelayCommand CancelCommand => new(() => pending?.Cancel());
    protected IProgress<string> Progress => new Progress<string>(message => { if (IsBusy) Status = message; });
    protected async Task Work(Func<CancellationToken, Task> operation)
    {
        if (IsBusy) return;
        using var cancellation = new CancellationTokenSource();
        pending = cancellation; IsBusy = true;
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { Status = "Operation cancelled. Completed imports are retained; refresh to see them."; }
        catch (Exception ex) { Status = "Operation failed: " + ex.Message; }
        finally { pending = null; IsBusy = false; }
    }
    protected static T Require<T>(T? service) where T : class => service ?? throw new InvalidOperationException("Import services are unavailable.");
}

public sealed partial class PackageManagerViewModel : ImportWorkspace
{
    private readonly ShellViewModel shell;
    private readonly IPackageImportService? importer;
    private readonly ICategoryService? categories;
    private readonly IImportDialogs? dialogs;
    public PackageManagerViewModel(ShellViewModel shell, IPackageImportService? importer, ICategoryService? categories, IImportDialogs? dialogs, NexInstaller.Core.Presets.IPresetService? presets = null)
        : base("Add an app", "Save an installer to your offline library.")
    {
        this.shell = shell; this.importer = importer; this.categories = categories; this.dialogs = dialogs;
        this.presets = presets;
        RebuildGroups([]);
        ImportCommand = new(ImportAsync, ex => Status = ex.Message);
        VerifyCommand = new(VerifyAsync, ex => Status = ex.Message);
        AddCategoryCommand = new(() => ChangeCategory("add"), ex => Status = ex.Message);
        RemoveCategoryCommand = new(() => ChangeCategory("remove"), ex => Status = ex.Message);
        AssignCategoryCommand = new(() => ChangeCategory("assign"), ex => Status = ex.Message);
        BrowseFileCommand = new(() => { var path = Require(dialogs).Installer(); if (path is not null) { SourcePath = path; EntryPoint = ""; Raise(nameof(EntryPoint)); } });
        BrowseFolderCommand = new(() => { var path = Require(dialogs).Folder(); if (path is not null) { SourcePath = path; EntryPoint = ""; Raise(nameof(EntryPoint)); } });
        BrowseEntryCommand = new(() => { if (!Directory.Exists(SourcePath)) { Status = "Select a source folder first."; return; } var path = Require(dialogs).EntryPoint(SourcePath); if (path is not null) { EntryPoint = Path.GetRelativePath(SourcePath, path); Raise(nameof(EntryPoint)); } });
    }
    private string sourcePath = "";
    public string SourcePath { get => sourcePath; set { sourcePath = value; Step = 0; Raise(); Raise(nameof(IsFolderSource)); Raise(nameof(FileDisplayName)); Raise(nameof(ReviewText)); } }
    private string entryPoint = "";
    public string EntryPoint { get => entryPoint; set { entryPoint = value; Raise(); Raise(nameof(ReviewText)); } }
    private string packageName = "";
    private string folder = "";
    public string Folder { get => folder; set { folder = value ?? ""; Raise(); Raise(nameof(ReviewText)); } }
    public ObservableCollection<string> Folders { get; } = [];
    public string PackageName { get => packageName; set { packageName = value; Raise(); Raise(nameof(ReviewText)); } }
    private string version = "";
    public string Version { get => version; set { version = value; Raise(); Raise(nameof(ReviewText)); } }
    private string category = "Tools";
    public string Category { get => category; set { if (value is null) return; category = value; Raise(); Raise(nameof(ReviewText)); SelectCurrentGroup(); } }
    private string description = "";
    public string Description { get => description; set { description = value; Raise(); Raise(nameof(ReviewText)); } }
    private bool requiresAdministrator = false;
    public bool RequiresAdministrator { get => requiresAdministrator; set { requiresAdministrator = value; Raise(); Raise(nameof(ReviewText)); } }
    private PackageArchitecture architecture = PackageArchitecture.X64;
    public PackageArchitecture Architecture { get => architecture; set { architecture = value; Raise(); Raise(nameof(ReviewText)); } }
    public IReadOnlyList<PackageArchitecture> Architectures { get; } = Enum.GetValues<PackageArchitecture>();
    public IReadOnlyList<string> Modes { get; } = ["Manual"];
    private string newCategory = "";
    public string NewCategory { get => newCategory; set { newCategory = value; Raise(); Raise(nameof(ReviewText)); } }
    public string? SelectedCategory { get; set; }
    public ObservableCollection<string> Categories { get; } = [];
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand VerifyCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand RemoveCategoryCommand { get; }
    public AsyncCommand AssignCategoryCommand { get; }
    public RelayCommand BrowseFileCommand { get; }
    public RelayCommand BrowseFolderCommand { get; }
    public RelayCommand BrowseEntryCommand { get; }
    public RelayCommand ClearSourceCommand => new(() =>
    {
        if (!IsIdle) return;
        SourcePath = ""; EntryPoint = ""; PackageName = ""; Version = ""; Description = "";
        Status = "Chosen file removed. Choose another installer to continue.";
    });
    public Task ImportAsync() => Work(async token =>
    {
        var request = new PackageImportRequest(SourcePath, EntryPoint, PackageName, Version, Architecture, Category, Description, RequiresAdministrator, Folder);
        if (!Require(dialogs).Confirm("Copy this source into the offline library? Installation will not start.")) { Status = "Import cancelled before copying."; return; }
        var result = await Require(importer).ImportAsync(request, Progress, token);
        await shell.RefreshAsync();
        Status = result.Message;
        shell.RecordOperation("package.import", result.Imported ? "Imported package." : "Duplicate package rejected.");
    });
    public Task VerifyAsync() => Work(async token =>
    {
        var results = await Require(importer).VerifyAsync(Progress, token);
        Status = results.Count == 0 ? "No package manifests to verify." : string.Join("\n", results.Select(result =>
            result.Success ? result.PackageId + ": SHA-256 passed" : result.PackageId + ": " + string.Join("; ", result.Problems)));
        shell.RecordOperation("package.verify", $"Verified {results.Count} packages; {results.Count(result => !result.Success)} failed.");
    });
    public async Task ReloadCategoriesAsync()
    {
        var destinationFolder = Folder;
        Folders.Clear();
        foreach (var name in shell.SoftwareLibrary.Folders.Where(option => !string.IsNullOrEmpty(option.Value))) Folders.Add(name.Value!);
        Folder = destinationFolder;
        if (categories is null) return;
        try
        {
            var names = await Task.Run(categories.List);
            Categories.Clear(); foreach (var name in names) Categories.Add(name);
            RebuildGroups(names);
        }
        catch (Exception ex) { Status = "Category list unavailable: " + ex.Message; }
    }
    private Task ChangeCategory(string action) => Work(async token =>
    {
        var service = Require(categories);
        // Snapshot bound fields before leaving the UI thread.
        var name = action == "add" ? NewCategory : SelectedCategory ?? "";
        var id = shell.SoftwareLibrary.SelectedPackage?.Package.Manifest.Id;
        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (action == "add") service.Add(name);
            else if (action == "remove") service.Remove(name);
            else service.Assign(id ?? throw new InvalidOperationException("Choose an app row in Apps first."), name);
        }, token);
        if (action == "remove" && string.Equals(Category, name, StringComparison.OrdinalIgnoreCase)) Category = "Tools";
        await shell.RefreshAsync();
        Status = "Group updated.";
        shell.RecordOperation("category.update", "Local category metadata updated.");
    });
}

public sealed class FontLibraryViewModel : ImportWorkspace
{
    public FolderToolsViewModel FolderTools { get; }
    public AsyncCommand? DeleteSelectedCommand { get; set; }
    public void ReportRemoval(string message) => Status = message;
    private readonly ShellViewModel shell;
    private readonly IFontImportService? importer;
    private readonly IImportDialogs? dialogs;
    public FontLibraryViewModel(ShellViewModel shell, IFontImportService? importer, IImportDialogs? dialogs)
        : base("Fonts", "Add licensed fonts and choose the ones this laptop needs.")
    {
        this.shell = shell; this.importer = importer; this.dialogs = dialogs;
        FolderTools = new(shell, LibrarySection.Fonts, () => SelectedFont is { } font ? [Path.GetRelativePath(shell.LibraryRoot, font.Path).Replace('\\', '/')] : [], message => Status = message);
        BrowseFilesCommand = new(ImportFilesAsync, ex => Status = ex.Message);
        BrowseFolderCommand = new(ImportFolderAsync, ex => Status = ex.Message);
    }
    public InstallationQueueViewModel InstallationQueue => shell.InstallationQueue;
    public RelayCommand QueueSelectedCommand => new(() => { if (SelectedFont is not null) InstallationQueue.AddFont(SelectedFont, shell.LibraryRoot, LicenseConfirmed); });
    private bool licenseConfirmed;
    public bool LicenseConfirmed { get => licenseConfirmed; set { licenseConfirmed = value; Raise(); } }
    public ObservableCollection<FontRecord> Fonts { get; } = [];
    private readonly List<FontRecord> allFonts = [];
    private string folder = "";
    public string Folder { get => folder; set { folder = value ?? ""; Raise(); } }
    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<CategoryOption> FolderFilters { get; } = [new("All folders", null)];
    private CategoryOption selectedFolder = new("All folders", null);
    public CategoryOption SelectedFolder { get => selectedFolder; set { selectedFolder = value ?? new("All folders", null); Raise(); FilterFonts(); if (selectedFolder.Value is { } name) Folder = name; } }
    public RelayCommand QueueFolderCommand => new(() =>
    {
        foreach (var font in Fonts) InstallationQueue.AddFont(font, shell.LibraryRoot, LicenseConfirmed);
    });
    private void FilterFonts()
    {
        var previous = SelectedFont?.Path;
        Fonts.Clear();
        foreach (var font in allFonts.Where(font => SelectedFolder.Value is null || string.Equals(font.Folder, SelectedFolder.Value, StringComparison.OrdinalIgnoreCase))) Fonts.Add(font);
        SelectedFont = Fonts.FirstOrDefault(font => font.Path == previous) ?? Fonts.FirstOrDefault();
    }
    private FontRecord? selected;
    public FontRecord? SelectedFont { get => selected; set { selected = value; Raise(); } }
    public AsyncCommand BrowseFilesCommand { get; }
    public AsyncCommand BrowseFolderCommand { get; }
    public async Task ReloadAsync()
    {
        if (importer is null) return;
        try
        {
            var folders = shell.LibraryFolders is null ? LibraryFolderInventory.Empty : await shell.LibraryFolders.ReadAsync(LibrarySection.Fonts);
            var fonts = (await importer.ListAsync()).Select(font => font with { Folder = folders.Resolve(Path.GetRelativePath(shell.LibraryRoot, font.Path).Replace('\\', '/'), font.Folder) }).ToArray();
            allFonts.Clear(); allFonts.AddRange(fonts);
            var previousFolder = SelectedFolder.Value;
            var destinationFolder = Folder;
            Folders.Clear();
            foreach (var name in fonts.Select(font => font.Folder).Concat(folders.Folders).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)) Folders.Add(name);
            FolderTools.Load(Folders);
            Folder = destinationFolder;
            FolderFilters.Clear(); FolderFilters.Add(new("All folders", null)); FolderFilters.Add(new("Unfiled fonts", ""));
            foreach (var name in Folders) FolderFilters.Add(new(name, name));
            selectedFolder = FolderFilters.FirstOrDefault(option => option.Value == previousFolder) ?? FolderFilters[0];
            Raise(nameof(SelectedFolder)); FilterFonts();
        }
        catch (Exception ex) { allFonts.Clear(); Fonts.Clear(); Folders.Clear(); SelectedFont = null; Status = "Cannot read fonts: " + ex.Message; }
    }
    public Task ImportFilesAsync() => ImportChosen(false);
    public Task ImportFolderAsync() => ImportChosen(true);
    private Task ImportChosen(bool folder) => Work(async token =>
    {
        IReadOnlyList<string> sources;
        if (folder) { var path = Require(dialogs).Folder(); sources = path is null ? [] : [path]; }
        else sources = Require(dialogs).Fonts();
        if (sources.Count == 0) { Status = "No font sources selected."; return; }
        if (!LicenseConfirmed)
        {
            if (!Require(dialogs).Confirm("Do the licenses for these fonts permit use on the destination devices? Confirm to copy the selected fonts into your library. No fonts will be installed yet."))
            { Status = "Font import cancelled. No files were copied because font licenses were not confirmed."; return; }
            LicenseConfirmed = true;
        }
        var results = await Require(importer).ImportAsync(sources, LicenseConfirmed, Progress, token, Folder);
        await shell.RefreshAsync();
        Status = string.Join("\n", results.Select(result => $"{Path.GetFileName(result.Source)}: {result.Status} — {result.Message}"));
        shell.RecordOperation("font.import", $"Font batch completed: {results.Count} results.");
    });
}
