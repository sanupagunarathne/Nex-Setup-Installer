using System.ComponentModel;
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
    public PackageManagerViewModel PackageManager { get; }
    public FontLibraryViewModel FontLibrary { get; }
    public SoftwareLibraryViewModel SoftwareLibrary { get; } = new();
    private readonly Dictionary<PageId, PageViewModel> pages;
    private readonly CancellationTokenSource lifetime = new();
    private LibrarySnapshot? snapshot;
    private bool busy;
    private bool refreshAgain;
    private string libraryStatus = "Library has not been scanned yet.";
    private string lastRefreshed = "Not scanned";

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }
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
    public string PresetCount => snapshot?.PresetFileCount.ToString("N0") ?? "—";
    public string SessionCount => snapshot?.SessionReportCount.ToString("N0") ?? "—";
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand OpenPackagesCommand { get; }
    public RelayCommand OpenPresetsCommand { get; }

    public ShellViewModel(NavigationState navigation, ILibrarySnapshotService inventory, LoadedConfiguration configuration, ILocalLog log, IPackageCatalogService catalogService, IPackageImportService? packageImports = null, IFontImportService? fontImports = null, ICategoryService? categories = null, IImportDialogs? dialogs = null)
    {
        this.navigation = navigation;
        this.inventory = inventory;
        this.log = log;
        this.catalogService = catalogService;
        Configuration = configuration;
        PackageManager = new(this, packageImports, categories, dialogs);
        FontLibrary = new(this, fontImports, dialogs);
        pages = new()
        {
            [PageId.Dashboard] = new DashboardViewModel(this),
            [PageId.SoftwareLibrary] = SoftwareLibrary,
            [PageId.FontLibrary] = FontLibrary,
            [PageId.Presets] = new PresetsViewModel(),
            [PageId.PackageManager] = PackageManager,
            [PageId.History] = new HistoryViewModel(),
            [PageId.Settings] = new SettingsViewModel(this)
        };
        NavigationItems =
        [
            new(PageId.Dashboard, "Dashboard", "\uE80F", navigation.Navigate),
            new(PageId.SoftwareLibrary, "Software Library", "\uE7F4", navigation.Navigate),
            new(PageId.FontLibrary, "Font Library", "\uE8D2", navigation.Navigate),
            new(PageId.Presets, "Installation Presets", "\uE8F1", navigation.Navigate),
            new(PageId.PackageManager, "Package Manager", "\uE7B8", navigation.Navigate),
            new(PageId.History, "Installation History", "\uE81C", navigation.Navigate),
            new(PageId.Settings, "Settings", "\uE713", navigation.Navigate)
        ];
        navigation.PropertyChanged += OnNavigationChanged;
        UpdateNavigation();
        RefreshCommand = new(RefreshAsync, ReportRefreshError);
        OpenPackagesCommand = new(() => navigation.Navigate(PageId.PackageManager));
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
                var notices = snapshot.Warnings.Concat(catalog.Issues.Select(issue => $"{issue.ManifestPath}: {issue.Message}")).ToArray();
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
