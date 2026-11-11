using System.Collections.ObjectModel;
using System.IO;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Packages;
namespace NexInstaller.App.ViewModels;

public interface IImportDialogs
{
    string? Installer();
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
    protected IProgress<string> Progress => new Progress<string>(message => Status = message);
    protected async Task Work(Func<CancellationToken, Task> operation)
    {
        if (IsBusy) return;
        using var cancellation = new CancellationTokenSource();
        pending = cancellation; IsBusy = true;
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { Status = "Operation cancelled. Any completed font imports are retained; refresh to see them."; }
        catch (Exception ex) { Status = "Operation failed: " + ex.Message; }
        finally { pending = null; IsBusy = false; }
    }
    protected static T Require<T>(T? service) where T : class => service ?? throw new InvalidOperationException("Import services are unavailable.");
}

public sealed class PackageManagerViewModel : ImportWorkspace
{
    private readonly ShellViewModel shell;
    private readonly IPackageImportService? importer;
    private readonly ICategoryService? categories;
    private readonly IImportDialogs? dialogs;
    public PackageManagerViewModel(ShellViewModel shell, IPackageImportService? importer, ICategoryService? categories, IImportDialogs? dialogs)
        : base("Package Manager", "Import local installers, verify hashes, and manage categories.")
    {
        this.shell = shell; this.importer = importer; this.categories = categories; this.dialogs = dialogs;
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
    public string SourcePath { get => sourcePath; set { sourcePath = value; Raise(); } }
    public string EntryPoint { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string Version { get; set; } = "";
    private string category = "Utilities";
    public string Category { get => category; set { if (value is null) return; category = value; Raise(); } }
    public string Description { get; set; } = "";
    public bool RequiresAdministrator { get; set; }
    public PackageArchitecture Architecture { get; set; } = PackageArchitecture.X64;
    public IReadOnlyList<PackageArchitecture> Architectures { get; } = Enum.GetValues<PackageArchitecture>();
    public IReadOnlyList<string> Modes { get; } = ["Manual"];
    public string NewCategory { get; set; } = "";
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
    public Task ImportAsync() => Work(async token =>
    {
        var request = new PackageImportRequest(SourcePath, EntryPoint, PackageName, Version, Architecture, Category, Description, RequiresAdministrator);
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
        if (categories is null) return;
        try
        {
            var names = await Task.Run(categories.List);
            Categories.Clear(); foreach (var name in names) Categories.Add(name);
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
            else service.Assign(id ?? throw new InvalidOperationException("Choose a package row in Software Library first."), name);
        }, token);
        await shell.RefreshAsync();
        Status = "Category updated.";
        shell.RecordOperation("category.update", "Local category metadata updated.");
    });
}

public sealed class FontLibraryViewModel : ImportWorkspace
{
    private readonly ShellViewModel shell;
    private readonly IFontImportService? importer;
    private readonly IImportDialogs? dialogs;
    public FontLibraryViewModel(ShellViewModel shell, IFontImportService? importer, IImportDialogs? dialogs)
        : base("Font Library", "Import licensed TTF/OTF files into your offline library.")
    {
        this.shell = shell; this.importer = importer; this.dialogs = dialogs;
        BrowseFilesCommand = new(() => ImportChosen(false), ex => Status = ex.Message);
        BrowseFolderCommand = new(() => ImportChosen(true), ex => Status = ex.Message);
    }
    public InstallationQueueViewModel InstallationQueue => shell.InstallationQueue;
    public RelayCommand QueueSelectedCommand => new(() => { if (SelectedFont is not null) InstallationQueue.AddFont(SelectedFont, shell.LibraryRoot, LicenseConfirmed); });
    public bool LicenseConfirmed { get; set; }
    public ObservableCollection<FontRecord> Fonts { get; } = [];
    private FontRecord? selected;
    public FontRecord? SelectedFont { get => selected; set { selected = value; Raise(); } }
    public AsyncCommand BrowseFilesCommand { get; }
    public AsyncCommand BrowseFolderCommand { get; }
    public async Task ReloadAsync()
    {
        if (importer is null) return;
        try
        {
            var fonts = await importer.ListAsync(); Fonts.Clear(); foreach (var font in fonts) Fonts.Add(font);
            SelectedFont = Fonts.FirstOrDefault();
        }
        catch (Exception ex) { Fonts.Clear(); SelectedFont = null; Status = "Cannot read fonts: " + ex.Message; }
    }
    private Task ImportChosen(bool folder) => Work(async token =>
    {
        if (!LicenseConfirmed) throw new InvalidOperationException("Confirm font licenses before selecting sources.");
        IReadOnlyList<string> sources;
        if (folder) { var path = Require(dialogs).Folder(); sources = path is null ? [] : [path]; }
        else sources = Require(dialogs).Fonts();
        if (sources.Count == 0) { Status = "No font sources selected."; return; }
        var results = await Require(importer).ImportAsync(sources, LicenseConfirmed, Progress, token);
        await shell.RefreshAsync();
        Status = string.Join("\n", results.Select(result => $"{Path.GetFileName(result.Source)}: {result.Status} — {result.Message}"));
        shell.RecordOperation("font.import", $"Font batch completed: {results.Count} results.");
    });
}
