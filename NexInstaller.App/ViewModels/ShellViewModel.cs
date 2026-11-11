using System.ComponentModel;
using NexInstaller.Core.Presets;
using NexInstaller.Core.History;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Library;
using NexInstaller.Core.Packages;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Infrastructure.Configuration;

namespace NexInstaller.App.ViewModels;

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly NavigationState navigation;
    private readonly ILibrarySnapshotService inventory;
    private readonly ILocalLog log;
    private readonly IPackageCatalogService catalogService;
    private PackageCatalog? catalog;
    public PresetsViewModel Presets { get; }
    public HistoryViewModel History { get; }
    public InstallationQueueViewModel InstallationQueue { get; }
    public PackageManagerViewModel PackageManager { get; }
    public FontLibraryViewModel FontLibrary { get; }
    public SoftwareLibraryViewModel SoftwareLibrary { get; } = new();
    private readonly Dictionary<PageId, PageViewModel> pages;
    private readonly CancellationTokenSource lifetime = new();
    private LibrarySnapshot? snapshot;
    private readonly bool presetsAvailable;
    private readonly bool historyAvailable;
    private bool busy;
    private bool refreshAgain;
    private string libraryStatus = "Library has not been scanned yet.";
    private string lastRefreshed = "Not scanned";

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }
    public bool IsAppsWorkbench => navigation.CurrentPage == PageId.SoftwareLibrary;
    public bool IsHome => navigation.CurrentPage == PageId.Dashboard;
    public RelayCommand OpenAppsCommand { get; }
    public RelayCommand OpenFontsCommand { get; }
    public PageViewModel CurrentPage => pages[navigation.CurrentPage];
    public LoadedConfiguration Configuration { get; }
    public string LibraryRoot => Configuration.Paths.Root;
    public string ConfigurationPath => Configuration.SourcePath;
    public string ConfigurationNotice => Configuration.Warning ?? "Configuration loaded. Paths are relative to the application unless an absolute library root is configured.";
    public string LogDirectory => log.LogDirectory ?? "Logging is unavailable. Both local locations may be read-only.";
    public string LibraryStatus { get => libraryStatus; private set { libraryStatus = value; Raise(); } }
    public string LastRefreshed { get => lastRefreshed; private set { lastRefreshed = value; Raise(); } }
    public bool IsBusy { get => busy; private set { busy = value; Raise(); } }
    public string AvailablePackageCount => catalog?.AvailableCount.ToString("N0") ?? "—";
    public string FontCount => snapshot?.FontFileCount.ToString("N0") ?? "—";
    public string PresetCount => presetsAvailable && Presets.IsLoaded ? Presets.Presets.Count.ToString("N0") : "—";
    public string SessionCount => historyAvailable && History.IsLoaded ? History.RecentCount.ToString("N0") : "—";
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand OpenPackagesCommand { get; }
    public RelayCommand OpenPresetsCommand { get; }

    public ShellViewModel(NavigationState navigation, ILibrarySnapshotService inventory, LoadedConfiguration configuration, ILocalLog log, IPackageCatalogService catalogService, IPackageImportService? packageImports = null, IFontImportService? fontImports = null, ICategoryService? categories = null, IImportDialogs? dialogs = null, IInstallationEngine? installationEngine = null, IPresetService? presetService = null, IHistoryService? historyService = null, IPresetsHistoryDialogs? presetsDialogs = null)
    {
        this.navigation = navigation;
        this.inventory = inventory;
        this.log = log;
        this.catalogService = catalogService;
        Configuration = configuration;
        InstallationQueue = new(installationEngine) { AfterRunAsync = RefreshAsync };
        presetsAvailable = presetService is not null; historyAvailable = historyService is not null;
        Presets = new(this, presetService, presetsDialogs);
        History = new(historyService, presetsDialogs);
        Presets.Presets.CollectionChanged += (_, _) => Raise(nameof(PresetCount));
        History.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(HistoryViewModel.RecentCount)) Raise(nameof(SessionCount)); };
        SoftwareLibrary.InstallationQueue = InstallationQueue;
        PackageManager = new(this, packageImports, categories, dialogs);
        FontLibrary = new(this, fontImports, dialogs);
        pages = new()
        {
            [PageId.Dashboard] = new DashboardViewModel(this),
            [PageId.SoftwareLibrary] = SoftwareLibrary,
            [PageId.FontLibrary] = FontLibrary,
            [PageId.Presets] = Presets,
            [PageId.PackageManager] = PackageManager,
            [PageId.History] = History,
            [PageId.Settings] = new SettingsViewModel(this)
        };
        NavigationItems =
        [
            new(PageId.Dashboard, "Home", "\uE80F", navigation.Navigate),
            new(PageId.SoftwareLibrary, "Apps", "\uE7F4", navigation.Navigate),
            new(PageId.FontLibrary, "Fonts", "\uE8D2", navigation.Navigate),
            new(PageId.Presets, "Saved setups", "\uE8F1", navigation.Navigate),
            new(PageId.PackageManager, "Add files", "\uE7B8", navigation.Navigate),
            new(PageId.History, "Past installs", "\uE81C", navigation.Navigate),
            new(PageId.Settings, "Settings", "\uE713", navigation.Navigate)
        ];
        navigation.PropertyChanged += OnNavigationChanged;
        UpdateNavigation();
        RefreshCommand = new(RefreshAsync, ReportRefreshError);
        OpenAppsCommand = new(() => navigation.Navigate(PageId.SoftwareLibrary));
        OpenFontsCommand = new(() => navigation.Navigate(PageId.FontLibrary));
        OpenPackagesCommand = new(() => navigation.Navigate(PageId.PackageManager));
        SoftwareLibrary.OpenAddFilesCommand = OpenPackagesCommand;
        OpenPresetsCommand = new(() => navigation.Navigate(PageId.Presets));
    }

    public async Task RefreshAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        if (IsBusy) { refreshAgain = true; return; }
        do
        {
            refreshAgain = false;
            IsBusy = true;
            LibraryStatus = "Reading local library…";
            try
            {
                var newSnapshot = await inventory.ReadAsync(lifetime.Token);
                var newCatalog = await catalogService.DiscoverAsync(lifetime.Token);
                snapshot = newSnapshot;
                catalog = newCatalog;
                SoftwareLibrary.Load(catalog);
                await PackageManager.ReloadCategoriesAsync();
                await FontLibrary.ReloadAsync();
                await Presets.ReloadAsync();
                await History.ReloadAsync();
                var notices = snapshot.Warnings.Concat(catalog.Issues.Select(issue => $"{issue.ManifestPath}: {issue.Message}")).Concat(Presets.Diagnostics).Concat(History.Diagnostics).ToArray();
                LibraryStatus = notices.Length == 0
                    ? $"Library folders read successfully. {catalog.AvailableCount} available of {catalog.Packages.Count} discovered software packages. Hashes are not yet verified."
                    : string.Join(Environment.NewLine, notices);
                LastRefreshed = DateTime.Now.ToString("g");
                foreach (var property in new[] { nameof(AvailablePackageCount), nameof(FontCount), nameof(PresetCount), nameof(SessionCount) })
                    Raise(property);
                log.TryWrite("library.scan", $"Read library inventory with {snapshot.Warnings.Count} warnings.");
                Raise(nameof(LogDirectory));
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { ReportRefreshError(ex); }
            finally { IsBusy = false; }
        } while (refreshAgain && !lifetime.IsCancellationRequested);
    }

    private void ReportRefreshError(Exception ex)
    {
        catalog = null;
        SoftwareLibrary.Load(new PackageCatalog([], [new CatalogIssue("library", "Refresh failed. Try refreshing again.")]));
        Raise(nameof(AvailablePackageCount));
        LibraryStatus = "The library could not be read. Check its location and permissions, then refresh.";
        log.TryWrite("library.scan.failed", "Local inventory scan failed.", ex);
        Raise(nameof(LogDirectory));
    }

    public void RecordOperation(string eventName, string message) => log.TryWrite(eventName, message);

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateNavigation();
        Raise(nameof(CurrentPage));
        Raise(nameof(IsHome));
        Raise(nameof(IsAppsWorkbench));
    }

    private void UpdateNavigation()
    {
        foreach (var item in NavigationItems)
            item.IsActive = item.Id == navigation.CurrentPage;
    }

    public void Dispose()
    {
        if (lifetime.IsCancellationRequested) return;
        navigation.PropertyChanged -= OnNavigationChanged;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
