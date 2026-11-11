using NexInstaller.App.ViewModels;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;

namespace NexInstaller.Tests;

public sealed class SoftwareLibraryTests
{
    private static CatalogPackage Package(string id, string name, string category, PackageArchitecture architecture, bool available = true)
    {
        var manifest = new JsonPackageManifestReader().Parse(ManifestTests.ValidJson(id)).Manifest!;
        return new(manifest with { Name = name, Category = category, Architecture = architecture }, "manifests/" + id + ".json", 1024, available ? [] : ["Missing file"]);
    }
    private static PackageCatalog Catalog() => new([
        Package("editor", "Office Editor", "Office", PackageArchitecture.X64),
        Package("reader", "Document Reader", "Office", PackageArchitecture.X86),
        Package("tool", "Utility", "Utilities", PackageArchitecture.Any, false)], []);

    [Fact]
    public void SearchCategoryAndArchitectureCompose()
    {
        var vm = new SoftwareLibraryViewModel();
        vm.Load(Catalog());
        vm.SelectedCategory = vm.Categories.Single(option => option.Value == "Office");
        Assert.Equal(2, vm.Packages.Count);
        vm.SelectedArchitecture = "X86";
        Assert.Equal("Document Reader", Assert.Single(vm.Packages).Name);
        vm.Search = "OFFICE";
        Assert.Empty(vm.Packages);
        Assert.True(vm.IsEmpty);
        vm.SelectedArchitecture = "X64";
        Assert.Equal("Office Editor", Assert.Single(vm.Packages).Name);
    }

    [Fact]
    public void SelectionSurvivesFilteringButCannotIncludeUnavailablePackages()
    {
        var vm = new SoftwareLibraryViewModel();
        vm.Load(Catalog());
        vm.SelectVisibleCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);
        vm.Search = "Utility";
        Assert.Single(vm.Packages).IsSelected = true;
        Assert.Equal(2, vm.SelectedCount);
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public void CategoryResetToleratesComboBoxClearingSelection()
    {
        var vm = new SoftwareLibraryViewModel();
        vm.Load(Catalog());
        // WPF can clear SelectedItem synchronously while the options are rebuilt.
        vm.Categories.Clear();
        vm.SelectedCategory = null!;
        Assert.Equal(3, vm.Packages.Count);
        vm.Load(Catalog());
        Assert.Null(vm.SelectedCategory.Value);
    }

    [Fact]
    public void RefreshRemovesStaleSelectionAndUpdatesCategoriesAndDiagnostics()
    {
        var vm = new SoftwareLibraryViewModel();
        vm.Load(Catalog());
        vm.SelectVisibleCommand.Execute(null);
        vm.Load(new([Package("editor", "Editor", "New category", PackageArchitecture.X64, false)], [new("bad.json", "Invalid manifest")]));
        Assert.Equal(0, vm.SelectedCount);
        Assert.Equal(2, vm.Categories.Count);
        Assert.Contains("Invalid manifest", vm.Issues);
        Assert.Contains("Missing file", vm.Details);
    }
}
