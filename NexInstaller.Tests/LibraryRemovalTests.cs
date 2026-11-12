using System.Text.Json;
using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;
using NexInstaller.Infrastructure.Presets;

namespace NexInstaller.Tests;

public sealed class LibraryRemovalTests
{
    private static LibraryStorage Storage(string root) => new(new LibraryPaths(root, "."));
    [Fact]
    public async Task DeletesSavedAppButKeepsSourceAndRecoverableCopy()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var original = source.Write("setup.exe", "payload");
        var imported = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(original) with { Folder = "Adobe" });
        await new LibraryRemovalService(Storage(library.Root)).RemovePackagesAsync([imported.Manifest!.Id]);
        Assert.True(File.Exists(original)); Assert.False(File.Exists(Path.Combine(library.Root, imported.Manifest.EntryPoint)));
        Assert.Empty(Storage(library.Root).ReadManifests());
        Assert.Single(Directory.GetFiles(Path.Combine(library.Root, ".trash"), "*.exe", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(Path.Combine(library.Root, ".trash"), "*.json", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task SharedFilesStayUntilLastReferencingPackageIsDeleted()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var result = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "payload")));
        var second = result.Manifest! with { Id = "second" };
        library.Write("manifests/second.json", JsonSerializer.Serialize(second, LibraryStorage.Json));
        var service = new LibraryRemovalService(Storage(library.Root));
        await service.RemovePackagesAsync([result.Manifest.Id]);
        Assert.True(File.Exists(Path.Combine(library.Root, second.EntryPoint))); Assert.Single(Storage(library.Root).ReadManifests());
        await service.RemovePackagesAsync([second.Id]);
        Assert.False(File.Exists(Path.Combine(library.Root, second.EntryPoint))); Assert.Empty(Storage(library.Root).ReadManifests());
    }
    [Fact]
    public async Task LockedManifestRollsBackAlreadyMovedPayload()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var result = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "payload")));
        var manifest = Path.Combine(library.Root, "manifests", result.Manifest!.Id + ".json");
        using var locked = File.Open(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAnyAsync<IOException>(() => new LibraryRemovalService(Storage(library.Root)).RemovePackagesAsync([result.Manifest.Id]));
        Assert.True(File.Exists(manifest)); Assert.True(File.Exists(Path.Combine(library.Root, result.Manifest.EntryPoint)));
    }
    [Fact]
    public async Task FontDeletionRejectsOutsideSourcesAndMovesOnlySelectedSavedFont()
    {
        using var library = new TemporaryDirectory(); using var outside = new TemporaryDirectory();
        var first = library.Write("fonts/folders/Brand/hash/one.ttf", "first");
        var second = library.Write("fonts/other/two.ttf", "second");
        var original = outside.Write("original.ttf", "outside"); var service = new LibraryRemovalService(Storage(library.Root));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RemoveFontAsync(original));
        await service.RemoveFontAsync(first);
        Assert.False(File.Exists(first)); Assert.True(File.Exists(second)); Assert.True(File.Exists(original));
        Assert.Single(Directory.GetFiles(Path.Combine(library.Root, ".trash"), "*.ttf", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task ConfirmedDeletionRefreshesLibraryAndQueueAndInvalidatesPresetReference()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var paths = new LibraryPaths(library.Root, "."); var storage = Storage(library.Root);
        var catalog = new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64);
        var presets = new JsonPresetService(paths, catalog);
        using var shell = new ShellViewModel(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null),
            new Log(), catalog, new PackageImportService(storage), dialogs: new Dialogs(true), presetService: presets, libraryRemoval: new LibraryRemovalService(storage));
        shell.PackageManager.CollectionName = "Apps"; shell.PackageManager.AddCollectionSource(source.Write("setup.exe", "payload"));
        await shell.PackageManager.SaveCollectionAsync();
        var preset = Assert.Single(shell.Presets.Presets);
        shell.SoftwareLibrary.SelectVisibleCommand.Execute(null); shell.SoftwareLibrary.AddToQueueCommand.Execute(null);
        Assert.Equal(1, shell.InstallationQueue.Count);
        await shell.DeleteSelectedAppsAsync();
        Assert.Empty(shell.SoftwareLibrary.Packages); Assert.Equal(0, shell.InstallationQueue.Count);
        Assert.False((await presets.ResolveAsync(preset.Id)).CanStart);
        Assert.False(shell.InstallationQueue.IsLibraryChanging);
    }
    [Fact]
    public async Task CancelledDeletionPreservesSavedFiles()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var paths = new LibraryPaths(library.Root, "."); var storage = Storage(library.Root);
        var imported = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "payload")));
        using var shell = new ShellViewModel(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null), new Log(),
            new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64), dialogs: new Dialogs(false), libraryRemoval: new LibraryRemovalService(storage));
        await shell.RefreshAsync(); await shell.DeleteSelectedAppsAsync();
        Assert.True(File.Exists(Path.Combine(library.Root, imported.Manifest!.EntryPoint))); Assert.Single(shell.SoftwareLibrary.Packages);
        shell.InstallationQueue.IsLibraryChanging = true;
        await Assert.ThrowsAsync<InvalidOperationException>(shell.DeleteSelectedAppsAsync);
        await shell.InstallationQueue.StartAsync(); Assert.Contains("library change", shell.InstallationQueue.Status);
        shell.InstallationQueue.IsLibraryChanging = false;
    }
    private sealed class Dialogs(bool confirm) : IImportDialogs
    {
        public string? Installer() => null; public string? Folder() => null; public string? EntryPoint(string directory) => null;
        public IReadOnlyList<string> Fonts() => []; public bool Confirm(string message) => confirm;
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null; public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
}
