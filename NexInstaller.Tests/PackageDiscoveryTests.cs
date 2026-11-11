using NexInstaller.Core.Configuration;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;

namespace NexInstaller.Tests;

public sealed class PackageDiscoveryTests
{
    private static FileSystemPackageCatalogService Service(TemporaryDirectory directory) =>
        new(new LibraryPaths(directory.Root, "."), new JsonPackageManifestReader(), PackageArchitecture.X64);

    [Fact]
    public async Task DiscoversAvailablePackagesAndActualSizeWithoutExecution()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("packages/editor/setup.exe", "dummy payload never executed");
        directory.Write("manifests/editor.json", ManifestTests.ValidJson());
        var catalog = await Service(directory).DiscoverAsync();
        var package = Assert.Single(catalog.Packages);
        Assert.True(package.IsAvailable);
        Assert.Equal(new FileInfo(Path.Combine(directory.Root, "packages/editor/setup.exe")).Length, package.SizeBytes);
        Assert.Equal(1, catalog.AvailableCount);
        Assert.Equal(["Utilities"], catalog.Categories);
        Assert.Empty(catalog.Issues);
    }

    [Fact]
    public async Task MissingPayloadRemainsVisibleAsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("manifests/editor.json", ManifestTests.ValidJson());
        var catalog = await Service(directory).DiscoverAsync();
        Assert.False(Assert.Single(catalog.Packages).IsAvailable);
        Assert.Equal(0, catalog.AvailableCount);
    }

    [Fact]
    public async Task DuplicateIdsAreAllUnavailableAndBadManifestDoesNotHideGoodOnes()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("packages/editor/setup.exe", "test");
        directory.Write("manifests/a.json", ManifestTests.ValidJson());
        directory.Write("manifests/b.json", ManifestTests.ValidJson());
        directory.Write("manifests/bad.json", "{");
        var catalog = await Service(directory).DiscoverAsync();
        Assert.Equal(2, catalog.Packages.Count);
        Assert.All(catalog.Packages, p => Assert.Contains(p.Problems, text => text.Contains("Duplicate package ID")));
        Assert.NotEmpty(catalog.Issues);
    }

    [Fact]
    public async Task IncompatibleArchitectureIsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("packages/editor/setup.exe", "test");
        directory.Write("manifests/a.json", ManifestTests.ValidJson().Replace("X64", "Arm64"));
        Assert.False(Assert.Single((await Service(directory).DiscoverAsync()).Packages).IsAvailable);
    }

    [Fact]
    public async Task MissingLibraryIsReportedAndNotCreated()
    {
        using var directory = new TemporaryDirectory();
        var catalog = await Service(directory).DiscoverAsync();
        Assert.Empty(catalog.Packages);
        Assert.NotEmpty(catalog.Issues);
        Assert.False(Directory.Exists(Path.Combine(directory.Root, "manifests")));
    }

    [Fact]
    public async Task RejectsLinkedPayloadDirectories()
    {
        using var directory = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        outside.Write("setup.exe", "outside payload");
        directory.Write("manifests/a.json", ManifestTests.ValidJson());
        Directory.CreateDirectory(Path.Combine(directory.Root, "packages"));
        // Windows creation needs developer mode/elevation; Linux exercises actual link rejection.
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateSymbolicLink(Path.Combine(directory.Root, "packages/editor"), outside.Root);
        var catalog = await Service(directory).DiscoverAsync();
        Assert.False(Assert.Single(catalog.Packages).IsAvailable);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(directory).DiscoverAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(PackageArchitecture.X86, true)]
    [InlineData(PackageArchitecture.X64, true)]
    [InlineData(PackageArchitecture.Any, true)]
    [InlineData(PackageArchitecture.Arm64, false)]
    public void ChecksCompatibilityForX64Host(PackageArchitecture architecture, bool expected) =>
        Assert.Equal(expected, ArchitectureCompatibility.IsCompatible(architecture, PackageArchitecture.X64));
}
