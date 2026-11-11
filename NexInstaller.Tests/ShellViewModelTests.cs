using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Library;
using NexInstaller.Core.Packages;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;

namespace NexInstaller.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void SidebarAndQuickActionsShareNavigationState()
    {
        using var shell = CreateShell(new FakeInventory());
        Assert.Equal(7, shell.NavigationItems.Count);
        Assert.IsType<DashboardViewModel>(shell.CurrentPage);
        shell.OpenPackagesCommand.Execute(null);
        Assert.IsType<PackageManagerViewModel>(shell.CurrentPage);
        Assert.Equal(PageId.PackageManager, Assert.Single(shell.NavigationItems, item => item.IsActive).Id);
        shell.OpenPresetsCommand.Execute(null);
        Assert.IsType<PresetsViewModel>(shell.CurrentPage);
        shell.NavigationItems.Single(item => item.Id == PageId.Settings).SelectCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task RefreshDisplaysActualServiceResults()
    {
        using var shell = CreateShell(new FakeInventory());
        Assert.Equal("—", shell.AvailablePackageCount);
        await shell.RefreshAsync();
        Assert.Equal("0", shell.AvailablePackageCount);
        Assert.Equal("7", shell.FontCount);
        Assert.Equal("—", shell.PresetCount);
        Assert.Equal("—", shell.SessionCount);
        Assert.False(shell.IsBusy);
        Assert.Contains("read successfully", shell.LibraryStatus);
    }

    [Fact]
    public async Task FailedRefreshCanBeRetried()
    {
        var inventory = new FakeInventory { Fail = true };
        using var shell = CreateShell(inventory);
        await shell.RefreshAsync();
        Assert.False(shell.IsBusy);
        Assert.Equal("—", shell.FontCount);
        Assert.Contains("could not be read", shell.LibraryStatus);
        inventory.Fail = false;
        await shell.RefreshAsync();
        Assert.Equal("7", shell.FontCount);
    }

    [Fact]
    public async Task DashboardAndSelectionFollowPayloadRemovalOnRefresh()
    {
        using var directory = new TemporaryDirectory();
        var payload = directory.Write("packages/editor/setup.exe", "dummy, never executed");
        directory.Write("manifests/editor.json", ManifestTests.ValidJson());
        var paths = new LibraryPaths(directory.Root, ".");
        using var shell = new ShellViewModel(new NavigationState(), new FileSystemLibrarySnapshotService(paths),
            new LoadedConfiguration(paths, "nexinstaller.json", null), new FakeLog(),
            new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64));
        await shell.RefreshAsync();
        Assert.Equal("1", shell.AvailablePackageCount);
        shell.SoftwareLibrary.SelectVisibleCommand.Execute(null);
        Assert.Equal(1, shell.SoftwareLibrary.SelectedCount);
        File.Delete(payload);
        await shell.RefreshAsync();
        Assert.Equal("0", shell.AvailablePackageCount);
        Assert.Equal(0, shell.SoftwareLibrary.SelectedCount);
        Assert.False(Assert.Single(shell.SoftwareLibrary.Packages).IsAvailable);
    }

    private static ShellViewModel CreateShell(ILibrarySnapshotService inventory) => new(
        new NavigationState(), inventory,
        new LoadedConfiguration(new LibraryPaths(Path.GetTempPath(), "package-library"), "nexinstaller.json", null), new FakeLog(), new FakeCatalog());

    private sealed class FakeInventory : ILibrarySnapshotService
    {
        public bool Fail { get; set; }
        public Task<LibrarySnapshot> ReadAsync(CancellationToken cancellationToken = default) => Fail
            ? Task.FromException<LibrarySnapshot>(new IOException("simulated removable drive failure"))
            : Task.FromResult(new LibrarySnapshot(3, 7, 2, 1, []));
    }

    private sealed class FakeCatalog : IPackageCatalogService
    {
        public Task<PackageCatalog> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PackageCatalog([], []));
    }

    private sealed class FakeLog : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
}
