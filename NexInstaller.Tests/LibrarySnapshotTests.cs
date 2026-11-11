using NexInstaller.Core.Configuration;
using NexInstaller.Infrastructure.Library;

namespace NexInstaller.Tests;

public sealed class LibrarySnapshotTests
{
    [Fact]
    public async Task CountsOnlySupportedFilesAndTopLevelMetadata()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("manifests/one.json", "{}");
        directory.Write("manifests/two.JSON", "{}");
        directory.Write("manifests/ignored.txt");
        directory.Write("manifests/nested/ignored.json");
        directory.Write("fonts/one.ttf");
        directory.Write("fonts/family/two.OTF");
        directory.Write("fonts/ignored.woff");
        directory.Write("presets/one.json", "{}");
        directory.Write("history/one.json", "{}");
        var snapshot = await new FileSystemLibrarySnapshotService(new LibraryPaths(directory.Root, ".")).ReadAsync();
        Assert.Equal(2, snapshot.ManifestCount);
        Assert.Equal(2, snapshot.FontFileCount);
        Assert.Equal(1, snapshot.PresetFileCount);
        Assert.Equal(1, snapshot.SessionReportCount);
        Assert.Empty(snapshot.Warnings);
    }

    [Fact]
    public async Task MissingLibraryIsReportedWithoutCreatingIt()
    {
        using var directory = new TemporaryDirectory();
        var paths = new LibraryPaths(directory.Root, "missing");
        var snapshot = await new FileSystemLibrarySnapshotService(paths).ReadAsync();
        Assert.Equal(0, snapshot.ManifestCount);
        Assert.Equal(4, snapshot.Warnings.Count);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public async Task FileInsteadOfDirectoryIsReportedWithoutCrashing()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("fonts", "not a folder");
        var snapshot = await new FileSystemLibrarySnapshotService(new LibraryPaths(directory.Root, ".")).ReadAsync();
        Assert.Equal(0, snapshot.FontFileCount);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("fonts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FileSystemLibrarySnapshotService(new LibraryPaths(directory.Root, ".")).ReadAsync(cancellation.Token));
    }
}
