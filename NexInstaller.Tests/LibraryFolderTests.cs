using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Packages;

namespace NexInstaller.Tests;

public sealed class LibraryFolderTests
{
    [Theory]
    [InlineData("../outside")]
    [InlineData("Adobe/Photoshop")]
    [InlineData("Adobe\\Photoshop")]
    [InlineData("C:\\Adobe")]
    [InlineData("CON")]
    [InlineData("LPT1.ttf")]
    [InlineData("Adobe.")]
    public async Task InvalidFoldersCannotPublishInstallerOrFonts(string folder)
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        await Assert.ThrowsAsync<ArgumentException>(() => PackageImportTests.Service(library.Root).ImportAsync(
            PackageImportTests.Request(source.Write("setup.exe", "dummy")) with { Folder = folder }));
        var fonts = new FontImportService(new LibraryStorage(new LibraryPaths(library.Root, ".")));
        await Assert.ThrowsAsync<ArgumentException>(() => fonts.ImportAsync([], true, folder: folder));
        Assert.Empty(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MultipleAppsShareNamedFolderAndRemainDiscoverableAfterReload()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var service = PackageImportTests.Service(library.Root);
        var first = await service.ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "first")) with { Folder = "Adobe" });
        var second = await service.ImportAsync(PackageImportTests.Request(source.Write("reader.msi", "second")) with { Folder = "Adobe" });
        Assert.True(first.Imported); Assert.True(second.Imported);
        Assert.StartsWith("packages/folders/Adobe/", first.Manifest!.EntryPoint);
        Assert.True(File.Exists(Path.Combine(library.Root, first.Manifest.EntryPoint)));
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(library.Root, "packages", "folders", "Adobe")).Length);
        var paths = new LibraryPaths(library.Root, ".");
        var catalog = await new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64).DiscoverAsync();
        Assert.Equal(2, catalog.AvailableCount);
        Assert.All(catalog.Packages, package => Assert.Equal("Adobe", package.Manifest.Folder));
        Assert.All(await service.VerifyAsync(), result => Assert.True(result.Success));
    }

    [Fact]
    public async Task FontFolderSurvivesServiceReloadAndLegacyFontsRemainUnfiled()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var bytes = new byte[28]; bytes[1] = 1; bytes[5] = 1;
        var first = source.Write("first.ttf"); File.WriteAllBytes(first, bytes);
        var service = new FontImportService(new LibraryStorage(new LibraryPaths(library.Root, ".")));
        Assert.Equal("Imported", Assert.Single(await service.ImportAsync([first], true, folder: "Brand fonts")).Status);
        bytes[8] = 1; var second = source.Write("second.otf"); File.WriteAllBytes(second, bytes);
        await service.ImportAsync([second], true);
        var reloaded = await new FontImportService(new LibraryStorage(new LibraryPaths(library.Root, "."))).ListAsync();
        Assert.Equal("Brand fonts", reloaded.Single(font => font.Name == "first.ttf").Folder);
        Assert.Equal("", reloaded.Single(font => font.Name == "second.otf").Folder);
        Assert.Contains(Path.Combine("fonts", "folders", "Brand fonts"), reloaded.Single(font => font.Name == "first.ttf").Path);
        Assert.Equal("Duplicate", Assert.Single(await service.ImportAsync([first], true, folder: "Brand fonts")).Status);
    }

    [Fact]
    public void FolderFiltersComposeAndKeepSelectionAcrossRefresh()
    {
        var manifest = new JsonPackageManifestReader().Parse(ManifestTests.ValidJson()).Manifest!;
        var catalog = new PackageCatalog([
            new(manifest with { Id = "adobe", Folder = "Adobe" }, "adobe.json", 1, []),
            new(manifest with { Id = "other" }, "other.json", 1, [])], []);
        var vm = new SoftwareLibraryViewModel(); vm.Load(catalog);
        vm.SelectedFolder = vm.Folders.Single(option => option.Value == "Adobe");
        vm.SelectVisibleCommand.Execute(null);
        Assert.Equal("adobe", Assert.Single(vm.Packages).Package.Manifest.Id);
        vm.Search = "nonexistent"; Assert.Empty(vm.Packages);
        vm.Search = ""; vm.Load(catalog);
        Assert.Equal("Adobe", vm.SelectedFolder.Value); Assert.Equal(1, vm.SelectedCount);
        vm.Folders.Clear(); vm.SelectedFolder = null!;
        Assert.Equal(2, vm.Packages.Count);
    }
}
