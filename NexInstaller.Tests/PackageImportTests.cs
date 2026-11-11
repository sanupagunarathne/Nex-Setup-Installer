using NexInstaller.Core.Configuration;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.Tests;
public sealed class PackageImportTests
{
    internal static PackageImportRequest Request(string path, string? entry = null) => new(path, entry, "Editor", "1.0", PackageArchitecture.X64, "Utilities");
    internal static PackageImportService Service(string root) => new(new LibraryStorage(new LibraryPaths(root, ".")));
    [Fact]
    public async Task ImportsFileAndRejectsRenamedDuplicateWithoutOverwriting()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var file = source.Write("setup.exe", "test payload");
        var service = Service(library.Root);
        var result = await service.ImportAsync(Request(file));
        Assert.True(result.Imported);
        Assert.Equal(InstallationMode.Manual, result.Manifest!.InstallationMode);
        Assert.Single(Directory.GetFiles(Path.Combine(library.Root, "manifests"), "*.json"));
        Assert.All(await service.VerifyAsync(), verification => Assert.True(verification.Success));
        Assert.False((await service.ImportAsync(Request(source.Write("renamed.exe", "test payload")))).Imported);
    }
    [Fact]
    public async Task FolderImportPreservesAllFilesAndDetectsTampering()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        source.Write("setup.msi", "msi dummy"); source.Write("data/files.bin", "support files");
        source.Write(".manifest.json", "a real source file must survive");
        var service = Service(library.Root);
        var result = await service.ImportAsync(Request(source.Root, "setup.msi"));
        Assert.Equal(3, result.Manifest!.Files.Length);
        var copied = Path.Combine(library.Root, result.Manifest.Files.Single(f => f.Path.EndsWith("files.bin")).Path);
        File.WriteAllText(copied, "modified");
        Assert.False(Assert.Single(await service.VerifyAsync()).Success);
    }
    [Fact]
    public async Task InvalidEntryAndNestedLibraryDoNotCommit()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        source.Write("setup.exe", "test");
        await Assert.ThrowsAsync<ArgumentException>(() => Service(library.Root).ImportAsync(Request(source.Root, "../setup.exe")));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(Path.Combine(source.Root, "library")).ImportAsync(Request(source.Root, "setup.exe")));
        Assert.False(Directory.Exists(Path.Combine(library.Root, "manifests")));
    }
    [Fact]
    public async Task CancellationAndBlockedDestinationDoNotCommit()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var file = source.Write("setup.exe", "test");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(library.Root).ImportAsync(Request(file), token: cancellation.Token));
        library.Write("packages", "blocks folder creation");
        await Assert.ThrowsAnyAsync<IOException>(() => Service(library.Root).ImportAsync(Request(file)));
        Assert.Empty(Directory.GetFiles(library.Root, "*.json", SearchOption.AllDirectories));
    }
}
