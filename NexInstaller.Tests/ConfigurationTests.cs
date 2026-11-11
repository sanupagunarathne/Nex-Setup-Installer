using NexInstaller.Core.Configuration;
using NexInstaller.Infrastructure.Configuration;

namespace NexInstaller.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ResolvesLibraryRelativeToApplicationAndNotWorkingDirectory()
    {
        using var directory = new TemporaryDirectory();
        var paths = new LibraryPaths(directory.Root, Path.Combine("library", "..", "portable"));
        Assert.Equal(Path.Combine(directory.Root, "portable"), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "packages"), paths.Packages);
        Assert.Equal(Path.Combine(paths.Root, "fonts"), paths.Fonts);
    }

    [Fact]
    public void SupportsConfiguredAbsoluteRoot()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Root, "external-library");
        Assert.Equal(root, new LibraryPaths(Path.Combine(directory.Root, "app"), root).Root);
    }

    [Fact]
    public void LoadsJsonConfiguration()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("nexinstaller.json", "{\"libraryRoot\":\"offline\"}");
        var configuration = ConfigurationLoader.Load(directory.Root);
        Assert.Null(configuration.Warning);
        Assert.Equal(Path.Combine(directory.Root, "offline"), configuration.Paths.Root);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"libraryRoot\":\" \"}")]
    [InlineData("{\"libraryRoot\":123}")]
    [InlineData("{\"libraryRoot\":\"library\",\"unknown\":true}")]
    public void InvalidConfigurationFallsBackWithoutCreatingFolders(string json)
    {
        using var directory = new TemporaryDirectory();
        directory.Write("nexinstaller.json", json);
        var configuration = ConfigurationLoader.Load(directory.Root);
        Assert.NotNull(configuration.Warning);
        Assert.Equal(Path.Combine(directory.Root, "package-library"), configuration.Paths.Root);
        Assert.False(Directory.Exists(configuration.Paths.Root));
    }

    [Fact]
    public void MissingConfigurationFallsBackWithWarning()
    {
        using var directory = new TemporaryDirectory();
        var configuration = ConfigurationLoader.Load(directory.Root);
        Assert.NotNull(configuration.Warning);
        Assert.False(File.Exists(configuration.SourcePath));
    }
}
