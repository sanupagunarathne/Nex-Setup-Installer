using NexInstaller.Core.Configuration;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.Tests;
public sealed class FontImportTests
{
    private static FontImportService Service(string root) => new(new LibraryStorage(new LibraryPaths(root, ".")));
    private static void WriteFont(string path)
    {
        // Structural fixture only; never registered with Windows or rendered.
        var bytes = new byte[28]; bytes[1] = 1; bytes[5] = 1;
        File.WriteAllBytes(path, bytes);
    }
    [Fact]
    public async Task ImportsMultipleFontsAndReportsDuplicateAndInvalidFiles()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        WriteFont(source.Write("one.ttf")); WriteFont(source.Write("two.ttf")); source.Write("bad.otf", "not a font");
        var service = Service(library.Root);
        var result = await service.ImportAsync([source.Root], true);
        Assert.Single(result, row => row.Status == "Imported");
        Assert.Single(result, row => row.Status == "Duplicate");
        Assert.Single(result, row => row.Status == "Failed");
        Assert.Single(await service.ListAsync());
    }
    [Fact]
    public async Task RequiresLicenseConfirmationAndHonorsCancellation()
    {
        using var library = new TemporaryDirectory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(library.Root).ImportAsync([], false));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(library.Root).ImportAsync([], true, token: cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(library.Root, "fonts")));
    }
}
