using System.Collections.ObjectModel;
using NexInstaller.Core.Packages;

namespace NexInstaller.App.ViewModels;

public sealed record CategoryOption(string Label, string? Value);

public sealed class PackageRowViewModel(CatalogPackage package, Action selectionChanged) : ObservableObject
{
    private bool selected;
    public CatalogPackage Package { get; } = package;
    public string Name => Package.Manifest.Name;
    public string Version => Package.Manifest.Version;
    public string Category => Package.Manifest.Category;
    public string Architecture => Package.Manifest.Architecture.ToString();
    public bool IsAvailable => Package.IsAvailable;
    public string Availability => IsAvailable ? "Available · hash unverified" : "Unavailable";
    public string Size => $"{Package.SizeBytes / 1024d / 1024d:N2} MiB";
    public bool IsSelected
    {
        get => selected;
        set
        {
            var next = value && IsAvailable;
            if (selected == next) return;
            selected = next;
            Raise();
            selectionChanged();
        }
    }
    public string Details => $"{Name} {Version}\nID: {Package.Manifest.Id}\nCategory: {Category}\nArchitecture: {Architecture}\nInstaller: {Package.Manifest.InstallerType}\nMode: {Package.Manifest.InstallationMode}\nAdministrator declared: {Package.Manifest.RequiresAdministrator}\nSize of readable files: {Size}\nManifest: {Package.ManifestPath}\nEntry point: {Package.Manifest.EntryPoint}\nProfile declaration: {Package.Manifest.VerifiedProfileId ?? "None"}\n\n{Package.Manifest.Description}\n\n" +
        (IsAvailable ? "Metadata and files passed Phase 2 checks. SHA-256 content verification is pending." : string.Join("\n", Package.Problems)) +
        "\n\nFiles (declared SHA-256; not yet verified):\n" + string.Join("\n", Package.Manifest.Files.Select(file => $"{file.Path}\n{file.Sha256}"));
}

public sealed class SoftwareLibraryViewModel() : PageViewModel("Software Library", "Discover, inspect, and select software in your offline library.")
{
    private readonly List<PackageRowViewModel> all = [];
    private string search = "";
    private string architecture = "All architectures";
    private CategoryOption category = new("All categories", null);
    private PackageRowViewModel? selectedPackage;
    private string issues = "Refresh the library to discover packages.";
    public ObservableCollection<PackageRowViewModel> Packages { get; } = [];
    public ObservableCollection<CategoryOption> Categories { get; } = [new("All categories", null)];
    public IReadOnlyList<string> Architectures { get; } = ["All architectures", "Any", "X86", "X64", "Arm64"];
    public string Search { get => search; set { search = value ?? ""; Raise(); Filter(); } }
    public string SelectedArchitecture { get => architecture; set { architecture = value ?? "All architectures"; Raise(); Filter(); } }
    public CategoryOption SelectedCategory { get => category; set { category = value ?? new CategoryOption("All categories", null); Raise(); Filter(); } }
    public PackageRowViewModel? SelectedPackage { get => selectedPackage; set { selectedPackage = value; Raise(); Raise(nameof(Details)); } }
    public string Details => SelectedPackage?.Details ?? "Select a row to inspect its metadata and availability.";
    public string Issues { get => issues; private set { issues = value; Raise(); } }
    public string Results => $"{Packages.Count} shown · {all.Count} discovered · {all.Count(row => row.IsAvailable)} available";
    public bool IsEmpty => Packages.Count == 0;
    public int SelectedCount => all.Count(row => row.IsSelected);
    public IReadOnlyList<PackageRowViewModel> SelectedPackages => all.Where(row => row.IsSelected).ToArray();
    public RelayCommand SelectVisibleCommand => new(() => { foreach (var row in Packages) row.IsSelected = true; });
    public RelayCommand ClearSelectionCommand => new(() => { foreach (var row in all) row.IsSelected = false; });

    public void Load(PackageCatalog catalog)
    {
        var previous = all.Where(row => row.IsSelected).Select(row => row.Package.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        all.Clear();
        foreach (var package in catalog.Packages)
        {
            var row = new PackageRowViewModel(package, SelectionChanged);
            all.Add(row);
            row.IsSelected = previous.Contains(package.Manifest.Id);
        }
        var previousCategory = SelectedCategory.Value;
        Categories.Clear();
        Categories.Add(new("All categories", null));
        foreach (var name in catalog.Categories) Categories.Add(new(name, name));
        category = Categories.FirstOrDefault(option => string.Equals(option.Value, previousCategory, StringComparison.OrdinalIgnoreCase)) ?? Categories[0];
        Raise(nameof(SelectedCategory));
        Issues = catalog.Issues.Count == 0 ? "No manifest parsing errors. Unavailable packages show their reasons in details."
            : string.Join("\n", catalog.Issues.Select(issue => $"{issue.ManifestPath}: {issue.Message}"));
        Filter();
        SelectionChanged();
    }

    private void Filter()
    {
        var query = Search.Trim();
        var visible = all.Where(row =>
            (SelectedCategory.Value is null || string.Equals(row.Category, SelectedCategory.Value, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedArchitecture == "All architectures" || row.Architecture == SelectedArchitecture) &&
            (query.Length == 0 || $"{row.Name} {row.Version} {row.Package.Manifest.Id} {row.Package.Manifest.Description}".Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        Packages.Clear();
        foreach (var row in visible) Packages.Add(row);
        if (SelectedPackage is null || !Packages.Contains(SelectedPackage)) SelectedPackage = Packages.FirstOrDefault();
        Raise(nameof(Results));
        Raise(nameof(IsEmpty));
    }

    private void SelectionChanged()
    {
        Raise(nameof(SelectedCount));
        Raise(nameof(SelectedPackages));
    }
}
