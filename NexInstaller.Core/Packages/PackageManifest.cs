namespace NexInstaller.Core.Packages;

public enum PackageArchitecture { Any, X86, X64, Arm64 }
public enum InstallerType { Exe, Msi }
public enum InstallationMode { Manual, VerifiedUnattended }

public sealed record PackageFile
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record PackageManifest
{
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required PackageArchitecture Architecture { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }
    public string? Folder { get; init; }
    public required InstallerType InstallerType { get; init; }
    public InstallationMode InstallationMode { get; init; } = InstallationMode.Manual;
    public string? VerifiedProfileId { get; init; }
    public bool RequiresAdministrator { get; init; }
    public required string EntryPoint { get; init; }
    public required PackageFile[] Files { get; init; }
}

public sealed record ManifestReadResult(PackageManifest? Manifest, IReadOnlyList<string> Errors)
{
    public bool IsValid => Manifest is not null && Errors.Count == 0;
}

public interface IPackageManifestReader
{
    ManifestReadResult Parse(string json);
}
