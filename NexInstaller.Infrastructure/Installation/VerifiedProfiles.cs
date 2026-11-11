using System.Text.Json;
using System.Text.Json.Serialization;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.Infrastructure.Installation;

public sealed record VerifiedInstallationProfile
{
    public required string Id { get; init; }
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public required PackageArchitecture Architecture { get; init; }
    public required InstallerType InstallerType { get; init; }
    public required string EntrySha256 { get; init; }
    public required string[] Arguments { get; init; }
    public required bool NoRestartVerified { get; init; }
    public required string VerificationNote { get; init; }
    public required DateTimeOffset VerifiedAtUtc { get; init; }
    public int[] SuccessExitCodes { get; init; } = [0];
    public int[] RestartExitCodes { get; init; } = [3010];
}
public sealed class VerifiedProfiles(string path)
{
    public (VerifiedInstallationProfile Profile, IDisposable Lease) Read(string id)
    {
        LibraryStorage.CheckPath(path);
        var lease = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (lease.Length > 128 * 1024) throw new InvalidDataException("Profile configuration exceeds 128 KiB.");
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
                Converters = { new JsonStringEnumConverter<PackageArchitecture>(allowIntegerValues: false), new JsonStringEnumConverter<InstallerType>(allowIntegerValues: false) } };
            var profiles = JsonSerializer.Deserialize<VerifiedInstallationProfile[]>(lease, options) ?? throw new InvalidDataException("Invalid profile configuration.");
            if (profiles.Any(p => p is null) || profiles.GroupBy(p => p.Id, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new InvalidDataException("Null or duplicate profiles are not allowed.");
            var profile = profiles.SingleOrDefault(p => p.Id == id) ?? throw new InvalidDataException("Verified installation profile was not found.");
            if (!profile.NoRestartVerified || string.IsNullOrWhiteSpace(profile.VerificationNote) || profile.VerifiedAtUtc == default || profile.VerifiedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
                profile.Arguments is null || profile.Arguments.Length > 64 || profile.Arguments.Any(a => a is null || a.Length > 2048 || a.Any(char.IsControl)) ||
                profile.SuccessExitCodes is null || profile.RestartExitCodes is null || profile.SuccessExitCodes.Length == 0 || profile.SuccessExitCodes.Concat(profile.RestartExitCodes).Any(c => c < 0 || c == 1641))
                throw new InvalidDataException("Profile needs explicit verification evidence, valid arguments/codes, and verified restart suppression.");
            return (profile, lease);
        }
        catch { lease.Dispose(); throw; }
    }
}
