using NexInstaller.Core.Configuration;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Packages;
using NexInstaller.Core.Presets;
using NexInstaller.Infrastructure.Packages;
using NexInstaller.Infrastructure.Presets;
namespace NexInstaller.Tests;
public sealed class PresetTests
{
    private static JsonPresetService Service(string root) { var paths = new LibraryPaths(root, "."); return new(paths, new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64)); }
    private static InstallationPreset Preset(params PresetItem[] items) => new() { SchemaVersion = 1, Id = Guid.NewGuid().ToString("N"), Name = "Office setup", Items = items };
    [Fact]
    public async Task SavesLoadsRenamesAndRemovesItems()
    {
        using var directory = new TemporaryDirectory(); var service = Service(directory.Root);
        var preset = Preset(new PresetItem() { Kind = InstallationKind.Software, PackageId = "editor" });
        await service.SaveAsync(preset);
        var loaded = Assert.Single((await service.ListAsync()).Presets); Assert.Equal(preset.Items, loaded.Items);
        await service.SaveAsync(loaded with { Name = "Renamed", Items = [] });
        loaded = Assert.Single((await service.ListAsync()).Presets); Assert.Equal("Renamed", loaded.Name); Assert.Empty(loaded.Items);
    }
    [Fact]
    public async Task MissingPackageBlocksStartingTheWholePreset()
    {
        using var directory = new TemporaryDirectory(); var service = Service(directory.Root);
        var preset = Preset(new PresetItem() { Kind = InstallationKind.Software, PackageId = "missing" }); await service.SaveAsync(preset);
        Assert.False((await service.ResolveAsync(preset.Id)).CanStart);
    }
    [Theory]
    [InlineData("../font.ttf")][InlineData("fonts/../font.ttf")][InlineData("C:/fonts/font.ttf")][InlineData("fonts/folder/NUL.ttf")]
    public void RejectsNonportablePaths(string path) => Assert.Throws<InvalidDataException>(() => JsonPresetService.Validate(Preset(new PresetItem() { Kind = InstallationKind.Font, FontRelativePath = path, Sha256 = new string('a', 64) })));
    [Fact]
    public async Task ReportsMalformedPresetsWithoutHidingValidOnes()
    {
        using var directory = new TemporaryDirectory(); var service = Service(directory.Root); await service.SaveAsync(Preset());
        directory.Write("presets/bad.json", "{"); var inventory = await service.ListAsync(); Assert.Single(inventory.Presets); Assert.Single(inventory.Warnings);
    }
    [Fact]
    public async Task FontContentChangeBlocksPreset()
    {
        using var directory = new TemporaryDirectory(); var file = directory.Write("fonts/font.ttf", "original");
        var hash = NexInstaller.Infrastructure.Importing.LibraryStorage.Hash(file, default);
        var preset = Preset(new PresetItem() { Kind = InstallationKind.Font, FontRelativePath = "fonts/font.ttf", Sha256 = hash }); var service = Service(directory.Root);
        await service.SaveAsync(preset); Assert.True((await service.ResolveAsync(preset.Id)).CanStart);
        File.WriteAllText(file, "modified"); Assert.False((await service.ResolveAsync(preset.Id)).CanStart);
    }
    [Fact]
    public void DuplicateItemsAreRejected()
    {
        var item = new PresetItem { Kind = InstallationKind.Software, PackageId = "editor" };
        Assert.Throws<InvalidDataException>(() => JsonPresetService.Validate(Preset(item, item)));
    }
    [Fact]
    public async Task BlockedFolderAndCancellationLeaveNoPartialJson()
    {
        using var directory = new TemporaryDirectory(); var service = Service(directory.Root);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(Preset(), cancelled.Token));
        directory.Write("presets", "blocked"); await Assert.ThrowsAnyAsync<IOException>(() => service.SaveAsync(Preset()));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.json", SearchOption.AllDirectories));
    }
}
