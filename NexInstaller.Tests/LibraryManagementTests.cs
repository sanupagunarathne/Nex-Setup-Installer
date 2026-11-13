using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Core.Packages;
using NexInstaller.Core.Presets;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;
using NexInstaller.Infrastructure.Presets;

namespace NexInstaller.Tests;

public sealed class LibraryManagementTests
{
    private sealed class Dialogs : IImportDialogs
    {
        public bool Confirmed { get; set; } = true;
        public string? Installer() => null;
        public string? Folder() => null;
        public string? EntryPoint(string directory) => null;
        public IReadOnlyList<string> Fonts() => [];
        public bool Confirm(string message) => Confirmed;
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
    private static ShellViewModel Shell(string root, Dialogs? dialogs = null)
    {
        var paths = new LibraryPaths(root, "."); var storage = new LibraryStorage(paths);
        var catalog = new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64);
        return new(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null), new Log(), catalog,
            packageImports: new PackageImportService(storage), fontImports: new FontImportService(storage), dialogs: dialogs ?? new(),
            presetService: new JsonPresetService(paths, catalog), libraryRemoval: new LibraryRemovalService(storage), libraryFolders: new LibraryFolderService(storage));
    }

    [Fact]
    public async Task EmptyFoldersPersistAndStaySeparateAcrossLibrarySections()
    {
        using var library = new TemporaryDirectory();
        using (var shell = Shell(library.Root))
        {
            await shell.RefreshAsync();
            shell.SoftwareLibrary.FolderTools!.NewFolderName = "Adobe";
            await shell.SoftwareLibrary.FolderTools.CreateAsync();
            Assert.Null(shell.SoftwareLibrary.SelectedFolder.Value);
            Assert.Contains(shell.SoftwareLibrary.FolderTools.BrowserFolders, folder => folder.Name == "Adobe");
            shell.SelectLibraryFolder(LibrarySection.Apps, "Adobe");
            Assert.Equal("Adobe", shell.PackageManager.Folder);
            Assert.Empty(shell.SoftwareLibrary.Packages);
            shell.FontLibrary.FolderTools.NewFolderName = "Client fonts";
            await shell.FontLibrary.FolderTools.CreateAsync();
            shell.Presets.FolderTools.NewFolderName = "Client setups";
            await shell.Presets.FolderTools.CreateAsync();
        }
        using var reloaded = Shell(library.Root); await reloaded.RefreshAsync();
        Assert.Contains(reloaded.SoftwareLibrary.Folders, option => option.Value == "Adobe");
        Assert.Contains("Adobe", reloaded.PackageManager.Folders);
        Assert.Contains("Client fonts", reloaded.FontLibrary.Folders);
        Assert.Contains(reloaded.Presets.FolderFilters, option => option.Value == "Client setups");
        Assert.DoesNotContain("Adobe", reloaded.FontLibrary.Folders);
        reloaded.SoftwareLibrary.SelectedFolder = reloaded.SoftwareLibrary.Folders.Single(option => option.Value == "Adobe");
        Assert.Equal("Adobe", reloaded.PackageManager.Folder);
        reloaded.FontLibrary.SelectedFolder = reloaded.FontLibrary.FolderFilters.Single(option => option.Value == "Client fonts");
        Assert.Equal("Client fonts", reloaded.FontLibrary.Folder);
    }

    [Fact]
    public async Task ReorganizingAppsAndFontsKeepsPayloadsQueueAndPresetReferencesValid()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var imported = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "payload")));
        var fontPath = library.Write("fonts/old/font.ttf", "font fixture");
        using var shell = Shell(library.Root); await shell.RefreshAsync();
        shell.SoftwareLibrary.SelectVisibleCommand.Execute(null); shell.SoftwareLibrary.AddToQueueCommand.Execute(null);
        shell.FontLibrary.LicenseConfirmed = true; shell.FontLibrary.QueueSelectedCommand.Execute(null);
        shell.Presets.AddSoftwareCommand.Execute(null); shell.Presets.AddFontCommand.Execute(null); await shell.Presets.SaveAsync();
        var presetId = shell.Presets.SelectedPreset!.Id;
        var tools = shell.SoftwareLibrary.FolderTools!;
        tools.NewFolderName = "Adobe"; await tools.CreateAsync();
        await tools.MoveAsync();
        Assert.Equal("Adobe", Assert.Single(shell.SoftwareLibrary.Packages).Folder);
        shell.FontLibrary.FolderTools.NewFolderName = "Necessary fonts"; await shell.FontLibrary.FolderTools.CreateAsync();
        // Creating a folder leaves the current files visible and selectable.
        shell.FontLibrary.SelectedFolder = shell.FontLibrary.FolderFilters[0];
        await shell.FontLibrary.FolderTools.MoveAsync();
        Assert.Equal("Necessary fonts", Assert.Single(shell.FontLibrary.Fonts).Folder);
        Assert.True(File.Exists(fontPath));
        Assert.True(File.Exists(Path.Combine(library.Root, imported.Manifest!.EntryPoint)));
        Assert.Equal(2, shell.InstallationQueue.Count);
        var catalog = new FileSystemPackageCatalogService(new(library.Root, "."), new JsonPackageManifestReader(), PackageArchitecture.X64);
        Assert.True((await new JsonPresetService(new(library.Root, "."), catalog).ResolveAsync(presetId)).CanStart);
        shell.FontLibrary.FolderTools.Destination = shell.FontLibrary.FolderTools.Destinations[0];
        await shell.FontLibrary.FolderTools.MoveAsync();
        Assert.Equal("", Assert.Single(shell.FontLibrary.Fonts).Folder);
        using var reloaded = Shell(library.Root); await reloaded.RefreshAsync();
        Assert.Equal("Adobe", Assert.Single(reloaded.SoftwareLibrary.Packages).Folder);
        Assert.Equal("", Assert.Single(reloaded.FontLibrary.Fonts).Folder);
    }

    [Fact]
    public async Task NewSetupsSaveInOpenFolderAndCanMoveWithoutLosingItems()
    {
        using var library = new TemporaryDirectory(); using var shell = Shell(library.Root);
        await shell.RefreshAsync();
        shell.Presets.FolderTools.NewFolderName = "Client setups"; await shell.Presets.FolderTools.CreateAsync();
        shell.SelectLibraryFolder(LibrarySection.Presets, "Client setups");
        shell.Presets.PresetName = "Design workstation";
        shell.Presets.Add(new() { Kind = InstallationKind.Software, PackageId = "editor" });
        await shell.Presets.SaveAsync();
        var id = Assert.Single(shell.Presets.VisiblePresets).Id;
        shell.Presets.FolderTools.Destination = shell.Presets.FolderTools.Destinations[0];
        await shell.Presets.FolderTools.MoveAsync();
        Assert.Equal("", shell.Presets.SelectedFolder.Value);
        Assert.Equal(id, Assert.Single(shell.Presets.VisiblePresets).Id);
        Assert.Single(shell.Presets.Items);
        shell.Presets.SelectedFolder = shell.Presets.FolderFilters.Single(option => option.Value == "Client setups");
        Assert.Empty(shell.Presets.VisiblePresets);
        Assert.Equal("1", shell.PresetCount);
    }

    [Fact]
    public async Task PresetDeletionCanBeCancelledThenConfirmedWithoutDeletingAssets()
    {
        using var library = new TemporaryDirectory(); var dialogs = new Dialogs();
        var font = library.Write("fonts/font.ttf", "fixture"); using var shell = Shell(library.Root, dialogs);
        await shell.RefreshAsync(); shell.FontLibrary.LicenseConfirmed = true; shell.FontLibrary.QueueSelectedCommand.Execute(null);
        shell.Presets.AddFontCommand.Execute(null); await shell.Presets.SaveAsync();
        var id = shell.Presets.SelectedPreset!.Id;
        shell.Presets.PresetName = "Unsaved name"; dialogs.Confirmed = false;
        await shell.DeleteSelectedPresetAsync();
        Assert.True(shell.Presets.IsDirty); Assert.Single(shell.Presets.Presets);
        dialogs.Confirmed = true; await shell.DeleteSelectedPresetAsync();
        Assert.Empty(shell.Presets.Presets); Assert.Empty(shell.Presets.VisiblePresets); Assert.False(shell.Presets.IsDirty);
        Assert.Equal("0", shell.PresetCount); Assert.True(File.Exists(font)); Assert.Equal(1, shell.InstallationQueue.Count);
        Assert.False(File.Exists(Path.Combine(library.Root, "presets", id + ".json")));
        Assert.Single(Directory.GetFiles(Path.Combine(library.Root, ".trash"), id + ".json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("CON")]
    [InlineData("")]
    public async Task InvalidFoldersCannotPublishMetadata(string name)
    {
        using var library = new TemporaryDirectory();
        var service = new LibraryFolderService(new(new(library.Root, ".")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(LibrarySection.Fonts, name));
        Assert.Empty(Directory.GetFiles(library.Root, "library-folders-*.json"));
    }

    [Fact]
    public async Task MalformedFolderMetadataIsNeverOverwrittenAndNamesAreCaseInsensitive()
    {
        using var library = new TemporaryDirectory();
        var service = new LibraryFolderService(new(new(library.Root, ".")));
        await service.CreateAsync(LibrarySection.Apps, "Adobe");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(LibrarySection.Apps, "adobe"));
        await service.AssignAsync(LibrarySection.Apps, ["editor"], "ADOBE");
        Assert.Equal("Adobe", (await service.ReadAsync(LibrarySection.Apps)).Resolve("EDITOR"));
        var path = library.Write("library-folders-fonts.json", "{");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.CreateAsync(LibrarySection.Fonts, "Fonts"));
        Assert.Equal("{", File.ReadAllText(path));
    }

    [Fact]
    public async Task ManagementIsBlockedWhileLibraryIsChanging()
    {
        using var library = new TemporaryDirectory(); using var shell = Shell(library.Root); await shell.RefreshAsync();
        shell.InstallationQueue.IsLibraryChanging = true; shell.FontLibrary.FolderTools.NewFolderName = "Fonts";
        await Assert.ThrowsAsync<InvalidOperationException>(shell.FontLibrary.FolderTools.CreateAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(shell.DeleteSelectedPresetAsync);
        shell.InstallationQueue.IsLibraryChanging = false;
        Assert.DoesNotContain("Fonts", shell.FontLibrary.Folders);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("")]
    public async Task PresetDeletionRejectsUnsafeIds(string id)
    {
        using var library = new TemporaryDirectory();
        var path = library.Write("presets/keep.json", "fixture");
        await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryRemovalService(new(new(library.Root, "."))).RemovePresetAsync(id));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task FolderDeletionCanBeCancelledAndPreservesFontsAcrossReload()
    {
        using var library = new TemporaryDirectory();
        var path = library.Write("fonts/folders/Client fonts/hash/example.ttf", "font fixture");
        var dialogs = new Dialogs(); using var shell = Shell(library.Root, dialogs); await shell.RefreshAsync();
        shell.SelectLibraryFolder(LibrarySection.Fonts, "Client fonts");
        Assert.False(shell.FontLibrary.FolderTools.BrowserFolders[0].CanDelete);
        dialogs.Confirmed = false;
        await shell.FontLibrary.FolderTools.DeleteAsync("Client fonts");
        Assert.Contains("Client fonts", shell.FontLibrary.Folders);
        dialogs.Confirmed = true;
        await shell.FontLibrary.FolderTools.DeleteAsync("Client fonts");
        Assert.DoesNotContain("Client fonts", shell.FontLibrary.Folders);
        Assert.Equal("", Assert.Single(shell.FontLibrary.Fonts).Folder);
        Assert.Equal("", shell.FontLibrary.Folder);
        Assert.True(File.Exists(path)); Assert.False(shell.InstallationQueue.IsLibraryChanging);
        await shell.RefreshAsync(); Assert.DoesNotContain("Client fonts", shell.FontLibrary.Folders);
        using var reloaded = Shell(library.Root); await reloaded.RefreshAsync();
        Assert.Equal("", Assert.Single(reloaded.FontLibrary.Fonts).Folder);
        Assert.DoesNotContain("Client fonts", reloaded.FontLibrary.Folders);
    }

    [Fact]
    public async Task DeletingImportedAppFolderKeepsAppsMovedElsewhereAndQueueReferences()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var importer = PackageImportTests.Service(library.Root);
        var first = await importer.ImportAsync(PackageImportTests.Request(source.Write("one.exe", "one")) with { Folder = "Adobe" });
        var second = await importer.ImportAsync(PackageImportTests.Request(source.Write("two.exe", "two")) with { Folder = "Adobe" });
        using var shell = Shell(library.Root); await shell.RefreshAsync();
        shell.SoftwareLibrary.SelectVisibleCommand.Execute(null); shell.SoftwareLibrary.AddToQueueCommand.Execute(null);
        await shell.LibraryFolders!.AssignAsync(LibrarySection.Apps, [second.Manifest!.Id], "Other");
        await shell.SoftwareLibrary.FolderTools!.DeleteAsync("Adobe");
        Assert.DoesNotContain(shell.SoftwareLibrary.Folders, option => option.Value == "Adobe");
        Assert.Equal("", shell.SoftwareLibrary.Packages.Single(row => row.Package.Manifest.Id == first.Manifest!.Id).Folder);
        Assert.Equal("Other", shell.SoftwareLibrary.Packages.Single(row => row.Package.Manifest.Id == second.Manifest.Id).Folder);
        Assert.Equal(2, shell.InstallationQueue.Count);
        Assert.All(await importer.VerifyAsync(), result => Assert.True(result.Success));
    }

    [Theory]
    [InlineData(LibrarySection.Apps)]
    [InlineData(LibrarySection.Fonts)]
    [InlineData(LibrarySection.Presets)]
    public async Task DeletingMetadataFolderClearsMembershipAndSupportsRecreation(LibrarySection section)
    {
        using var library = new TemporaryDirectory();
        var service = new LibraryFolderService(new(new(library.Root, ".")));
        await service.CreateAsync(section, "Clients");
        await service.AssignAsync(section, ["item"], "Clients");
        await service.DeleteAsync(section, "clients");
        var inventory = await service.ReadAsync(section);
        Assert.Empty(inventory.Folders); Assert.Equal("", inventory.Resolve("item", "Clients"));
        await service.CreateAsync(section, "Clients");
        Assert.Equal("", (await service.ReadAsync(section)).Resolve("item", "Clients"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(section, ""));
    }
}
