using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Tests;
public sealed class ImportWorkspaceTests
{
    private static ShellViewModel Shell(string root, bool confirm)
    {
        var paths = new LibraryPaths(root, "."); var storage = new LibraryStorage(paths);
        return new(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null),
            new FakeLog(), new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64),
            new PackageImportService(storage), new FontImportService(storage), new CategoryService(storage), new Dialogs(confirm));
    }
    [Fact]
    public async Task CancelledConfirmationDoesNotWriteLibrary()
    {
        using var library = new TemporaryDirectory(); using var shell = Shell(library.Root, false);
        await shell.PackageManager.ImportAsync();
        Assert.Empty(Directory.GetFiles(library.Root));
        Assert.Contains("cancelled", shell.PackageManager.Status);
        Assert.False(shell.PackageManager.IsBusy);
    }
    [Fact]
    public async Task ImportUpdatesDashboardAndReportsSuccess()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(library.Root, true);
        shell.PackageManager.SourcePath = source.Write("setup.exe", "test payload");
        shell.PackageManager.PackageName = "Editor"; shell.PackageManager.Version = "1.0";
        await shell.PackageManager.ImportAsync();
        Assert.Equal("1", shell.AvailablePackageCount);
        Assert.Contains("imported", shell.PackageManager.Status);
        Assert.False(shell.PackageManager.IsBusy);
    }
    [Fact]
    public async Task SavedFoldersReloadAndFontFolderQueuesOnlyItsContents()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(library.Root, true);
        shell.PackageManager.SourcePath = source.Write("setup.exe", "dummy");
        shell.PackageManager.PackageName = "Editor"; shell.PackageManager.Version = "1.0"; shell.PackageManager.Folder = "Adobe";
        await shell.PackageManager.ImportAsync();
        Assert.Contains("Adobe", shell.PackageManager.Folders);
        Assert.Equal("Adobe", shell.PackageManager.Folder);
        var fonts = new FontImportService(new LibraryStorage(new LibraryPaths(library.Root, ".")));
        var bytes = new byte[28]; bytes[1] = 1; bytes[5] = 1;
        var first = source.Write("one.ttf"); File.WriteAllBytes(first, bytes);
        await fonts.ImportAsync([first], true, folder: "Brand fonts");
        bytes[8] = 1; var second = source.Write("two.ttf"); File.WriteAllBytes(second, bytes);
        await fonts.ImportAsync([second], true);
        await shell.RefreshAsync();
        var vm = shell.FontLibrary;
        Assert.Contains("Brand fonts", vm.Folders);
        vm.SelectedFolder = vm.FolderFilters.Single(option => option.Value == "Brand fonts");
        Assert.Equal("one.ttf", Assert.Single(vm.Fonts).Name);
        vm.QueueFolderCommand.Execute(null); Assert.Equal(0, shell.InstallationQueue.Count);
        vm.LicenseConfirmed = true; vm.QueueFolderCommand.Execute(null);
        var request = Assert.Single(shell.InstallationQueue.Items).Request;
        Assert.StartsWith("fonts/folders/Brand fonts/", request.FontRelativePath!.Replace('\\', '/'));
        await shell.RefreshAsync(); Assert.Equal("Brand fonts", vm.SelectedFolder.Value);
        vm.FolderFilters.Clear(); vm.SelectedFolder = null!; Assert.Equal(2, vm.Fonts.Count);
    }
    [Fact]
    public async Task SourceChangeDuringCopyRollsBackPayloadAndManifest()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var path = source.Write("setup.exe", "original");
        await Assert.ThrowsAsync<IOException>(() => PackageImportTests.Service(library.Root).ImportAsync(
            PackageImportTests.Request(path), new InlineProgress(message => { if (message.StartsWith("Copying", StringComparison.Ordinal)) File.WriteAllText(path, "changed"); })));
        Assert.Empty(Directory.GetFiles(library.Root, "*.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(library.Root, "*.exe", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task MidCopyCancellationLeavesNoPublishedPackage()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackageImportTests.Service(library.Root).ImportAsync(
            PackageImportTests.Request(source.Write("setup.exe", "test")),
            new InlineProgress(message => { if (message.StartsWith("Copying", StringComparison.Ordinal)) cancellation.Cancel(); }), cancellation.Token));
        Assert.Empty(Directory.GetFiles(library.Root, "*.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(library.Root, "*.exe", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task LinkedSourcesAreRejected()
    {
        if (OperatingSystem.IsWindows()) return;
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var target = source.Write("real.exe", "test"); var link = System.IO.Path.Combine(source.Root, "link.exe");
        File.CreateSymbolicLink(link, target);
        await Assert.ThrowsAnyAsync<IOException>(() => PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(link)));
        Assert.Empty(Directory.GetFiles(library.Root));
    }
    [Fact]
    public async Task ImportDuringDiscoverySchedulesAnotherRefresh()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var paths = new LibraryPaths(library.Root, "."); var storage = new LibraryStorage(paths);
        var delayed = new DelayedCatalog(new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64));
        using var shell = new ShellViewModel(new NavigationState(), new FileSystemLibrarySnapshotService(paths),
            new LoadedConfiguration(paths, "nexinstaller.json", null), new FakeLog(), delayed,
            new PackageImportService(storage), dialogs: new Dialogs(true));
        var scan = shell.RefreshAsync();
        await delayed.Started.Task;
        try
        {
            shell.PackageManager.SourcePath = source.Write("setup.exe", "test");
            shell.PackageManager.PackageName = "Editor"; shell.PackageManager.Version = "1.0";
            await shell.PackageManager.ImportAsync();
        }
        finally { delayed.First.TrySetResult(new PackageCatalog([], [])); }
        await scan;
        Assert.Equal("1", shell.AvailablePackageCount);
    }
    private sealed class DelayedCatalog(IPackageCatalogService inner) : IPackageCatalogService
    {
        private bool first = true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PackageCatalog> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PackageCatalog> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            if (!first) return inner.DiscoverAsync(cancellationToken);
            first = false; Started.SetResult(); return First.Task;
        }
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    private sealed class FakeLog : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
    private sealed class Dialogs(bool confirm) : IImportDialogs
    {
        public string? Installer() => null; public string? Folder() => null;
        public string? EntryPoint(string directory) => null; public IReadOnlyList<string> Fonts() => [];
        public bool Confirm(string message) => confirm;
    }
}
