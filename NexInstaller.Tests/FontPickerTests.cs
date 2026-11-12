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

public sealed class FontPickerTests
{
    private static ShellViewModel Shell(string root, Dialogs dialogs)
    {
        var paths = new LibraryPaths(root, ".");
        return new(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null),
            new Log(), new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64),
            fontImports: new FontImportService(new LibraryStorage(paths)), dialogs: dialogs);
    }
    private static string Font(TemporaryDirectory source)
    {
        var path = source.Write("nested/test.ttf"); var bytes = new byte[28]; bytes[1] = 1; bytes[5] = 1;
        File.WriteAllBytes(path, bytes); return path;
    }
    private static Task Browse(FontLibraryViewModel vm, bool folder) => folder ? vm.ImportFolderAsync() : vm.ImportFilesAsync();
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UncheckedLicenseStillOpensPickerAndConfirmationThenImports(bool folder)
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var path = Font(source); var dialogs = new Dialogs { Files = [path], Directory = source.Root, AllowLicense = true };
        using var shell = Shell(library.Root, dialogs); var vm = shell.FontLibrary; vm.Folder = "Brand fonts";
        var changed = new List<string?>(); vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await Browse(vm, folder);
        Assert.Equal(new[] { folder ? "folder" : "files", "license" }, dialogs.Events);
        Assert.True(vm.LicenseConfirmed); Assert.Contains(nameof(vm.LicenseConfirmed), changed);
        Assert.Equal("Brand fonts", Assert.Single(vm.Fonts).Folder); Assert.Equal("1", shell.FontCount);
        Assert.Contains("Imported", vm.Status); Assert.True(vm.IsIdle); Assert.Equal(0, shell.InstallationQueue.Count);
        await Browse(vm, folder);
        Assert.Single(vm.Fonts); Assert.Contains("Duplicate", vm.Status);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancellingPickerDoesNotAskForLicenseOrWriteFiles(bool folder)
    {
        using var library = new TemporaryDirectory(); var dialogs = new Dialogs(); using var shell = Shell(library.Root, dialogs);
        await Browse(shell.FontLibrary, folder);
        Assert.Equal(new[] { folder ? "folder" : "files" }, dialogs.Events);
        Assert.Empty(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories));
        Assert.False(shell.FontLibrary.LicenseConfirmed); Assert.True(shell.FontLibrary.IsIdle);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DecliningLicenseAfterChoosingDoesNotCopyFonts(bool folder)
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var path = Font(source); var dialogs = new Dialogs { Files = [path], Directory = source.Root };
        using var shell = Shell(library.Root, dialogs); await Browse(shell.FontLibrary, folder);
        Assert.Equal(new[] { folder ? "folder" : "files", "license" }, dialogs.Events);
        Assert.Empty(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories));
        Assert.False(shell.FontLibrary.LicenseConfirmed); Assert.Contains("cancelled", shell.FontLibrary.Status); Assert.True(shell.FontLibrary.IsIdle);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExistingAcknowledgementDoesNotPromptAgain(bool folder)
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var path = Font(source); var dialogs = new Dialogs { Files = [path], Directory = source.Root };
        using var shell = Shell(library.Root, dialogs); shell.FontLibrary.LicenseConfirmed = true;
        await Browse(shell.FontLibrary, folder);
        Assert.Equal(new[] { folder ? "folder" : "files" }, dialogs.Events); Assert.Single(shell.FontLibrary.Fonts);
    }
    [Fact]
    public async Task InvalidFontReportsFailureAndLeavesButtonsUsable()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var dialogs = new Dialogs { Files = [source.Write("bad.ttf", "invalid font")], AllowLicense = true };
        using var shell = Shell(library.Root, dialogs); await shell.FontLibrary.ImportFilesAsync();
        Assert.Empty(shell.FontLibrary.Fonts); Assert.Contains("Failed", shell.FontLibrary.Status); Assert.True(shell.FontLibrary.IsIdle);
        dialogs.Files = [Font(source)]; await shell.FontLibrary.ImportFilesAsync(); Assert.Single(shell.FontLibrary.Fonts);
    }
    private sealed class Dialogs : IImportDialogs
    {
        public IReadOnlyList<string> Files { get; set; } = [];
        public string? Directory { get; set; }
        public bool AllowLicense { get; init; }
        public List<string> Events { get; } = [];
        public string? Installer() => null;
        public string? Folder() { Events.Add("folder"); return Directory; }
        public string? EntryPoint(string directory) => null;
        public IReadOnlyList<string> Fonts() { Events.Add("files"); return Files; }
        public bool Confirm(string message) { Events.Add("license"); return AllowLicense; }
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
}
