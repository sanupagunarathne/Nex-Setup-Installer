namespace NexInstaller.Core.Packages;

public sealed record CatalogPackage(PackageManifest Manifest, string ManifestPath, long SizeBytes, IReadOnlyList<string> Problems)
{
    public bool IsAvailable => Problems.Count == 0;
}
public sealed record CatalogIssue(string ManifestPath, string Message);
public sealed record PackageCatalog(IReadOnlyList<CatalogPackage> Packages, IReadOnlyList<CatalogIssue> Issues)
{
    public int AvailableCount => Packages.Count(package => package.IsAvailable);
    public IReadOnlyList<string> Categories => Packages.Select(p => p.Manifest.Category)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
}
public interface IPackageCatalogService
{
    Task<PackageCatalog> DiscoverAsync(CancellationToken cancellationToken = default);
}

public static class ArchitectureCompatibility
{
    public static bool IsCompatible(PackageArchitecture package, PackageArchitecture host) =>
        package == PackageArchitecture.Any || package == host || (host == PackageArchitecture.X64 && package == PackageArchitecture.X86);
}
