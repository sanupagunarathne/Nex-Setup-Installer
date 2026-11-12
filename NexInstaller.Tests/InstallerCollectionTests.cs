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

public sealed class InstallerCollectionTests
{
    private static ShellViewModel Shell(string root, bool confirm = true)
    {
        var paths = new LibraryPaths(root, "."); var storage = new LibraryStorage(paths);
        var catalog = new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64);
        return new(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null),
            new Log(), catalog, new PackageImportService(storage), new FontImportService(storage), new CategoryService(storage), new Dialogs(confirm),
            presetService: new JsonPresetService(paths, catalog));
    }
    [Fact]
    public async Task BulkImportSavesFolderAndResolvablePresetWithoutDuplicatingOnRetry()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(library.Root);
        var vm = shell.PackageManager; vm.CollectionName = "Adobe products";
        vm.AddCollectionSource(source.Write("photo.exe", "photo payload")); vm.AddCollectionSource(source.Write("reader.msi", "reader payload"));
        vm.CollectionInstallers[0].Name = "Photo editor"; vm.CollectionInstallers[0].Version = "2026";
        await vm.SaveCollectionAsync();
        var preset = Assert.Single(shell.Presets.Presets); Assert.Equal("Adobe products", preset.Name); Assert.Equal(2, preset.Items.Length);
        Assert.Equal(2, shell.SoftwareLibrary.Packages.Count);
        Assert.All(shell.SoftwareLibrary.Packages, package => Assert.Equal("Adobe products", package.Folder));
        Assert.Contains(shell.SoftwareLibrary.Packages, package => package.Name == "Photo editor" && package.Version == "2026");
        Assert.Equal(0, shell.InstallationQueue.Count);
        var paths = new LibraryPaths(library.Root, ".");
        var service = new JsonPresetService(paths, new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64));
        Assert.True((await service.ResolveAsync(preset.Id)).CanStart);
        await vm.SaveCollectionAsync(); Assert.Equal(preset.Id, Assert.Single(shell.Presets.Presets).Id);
        Assert.Equal(2, shell.SoftwareLibrary.Packages.Count); Assert.Contains("2 reused", vm.Status);
    }
    [Fact]
    public async Task IncompleteBatchKeepsCompletedImportsAndCanBeRetried()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(library.Root);
        var vm = shell.PackageManager; vm.CollectionName = "Client setup";
        vm.AddCollectionSource(source.Write("one.exe", "one"));
        var missing = Path.Combine(source.Root, "two.exe"); vm.AddCollectionSource(missing);
        await vm.SaveCollectionAsync();
        Assert.Single(shell.SoftwareLibrary.Packages); Assert.Empty(shell.Presets.Presets); Assert.Contains("1 failed", vm.Status);
        source.Write("two.exe", "two"); await vm.SaveCollectionAsync();
        Assert.Equal(2, Assert.Single(shell.Presets.Presets).Items.Length);
    }
    [Fact]
    public async Task CollectionCanBeSavedWithoutPresetAndCancelledBeforeCopying()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var cancelled = Shell(library.Root, false);
        cancelled.PackageManager.CollectionName = "Apps"; cancelled.PackageManager.AddCollectionSource(source.Write("one.exe", "one"));
        await cancelled.PackageManager.SaveCollectionAsync(); Assert.Empty(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories));
        using var shell = Shell(library.Root); var vm = shell.PackageManager;
        vm.CollectionName = "Apps"; vm.SaveCollectionAsPreset = false; vm.AddCollectionSource(Path.Combine(source.Root, "one.exe"));
        await vm.SaveCollectionAsync(); Assert.Single(shell.SoftwareLibrary.Packages); Assert.Empty(shell.Presets.Presets);
    }
    [Fact]
    public async Task CompleteFolderSupportsMultipleDistinctInstallerEntryPoints()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(library.Root);
        source.Write("one.exe", "first setup"); source.Write("two.exe", "second setup"); source.Write("data/payload.bin", "support");
        var vm = shell.PackageManager; vm.CollectionName = "Suite";
        vm.AddCollectionSource(source.Root, "one.exe"); vm.AddCollectionSource(source.Root, "two.exe");
        await vm.SaveCollectionAsync();
        Assert.Equal(2, Assert.Single(shell.Presets.Presets).Items.Length);
        Assert.All(shell.SoftwareLibrary.Packages, row => Assert.Equal(3, row.Package.Manifest.Files.Length));
    }
    [Fact]
    public async Task InvalidCollectionNameAndMissingMetadataDoNotWrite()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(library.Root);
        var vm = shell.PackageManager; vm.AddCollectionSource(source.Write("setup.exe", "dummy"));
        vm.CollectionName = "../outside"; await vm.SaveCollectionAsync();
        Assert.Empty(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories));
        vm.CollectionName = "Apps"; vm.CollectionInstallers[0].Version = ""; await vm.SaveCollectionAsync();
        Assert.Empty(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories));
    }
    private sealed class Dialogs(bool confirm) : IImportDialogs
    {
        public string? Installer() => null;
        public string? Folder() => null;
        public string? EntryPoint(string directory) => null;
        public IReadOnlyList<string> Fonts() => [];
        public bool Confirm(string message) => confirm;
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
}
