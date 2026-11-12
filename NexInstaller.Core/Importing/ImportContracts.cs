using NexInstaller.Core.Packages;
namespace NexInstaller.Core.Importing;

public sealed record PackageImportRequest(string SourcePath, string? EntryPoint, string Name, string Version,
    PackageArchitecture Architecture, string Category, string? Description = null, bool RequiresAdministrator = false, string? Folder = null);
public sealed record PackageImportResult(bool Imported, string Message, PackageManifest? Manifest = null);
public sealed record VerificationResult(string PackageId, IReadOnlyList<string> Problems)
{
    public bool Success => Problems.Count == 0;
}
public interface IPackageImportService
{
    Task<PackageImportResult> ImportAsync(PackageImportRequest request, IProgress<string>? progress = null, CancellationToken token = default);
    Task<IReadOnlyList<VerificationResult>> VerifyAsync(IProgress<string>? progress = null, CancellationToken token = default);
}
