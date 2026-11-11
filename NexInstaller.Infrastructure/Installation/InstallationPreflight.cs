using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Infrastructure.Installation;

public interface IInstallationEnvironment
{
    bool IsSupported { get; }
    bool IsAdministrator { get; }
    PackageArchitecture Architecture { get; }
    string MsiExecPath { get; }
}
public sealed class InstallationPreflight(LibraryPaths paths, VerifiedProfiles profiles, IInstallationEnvironment environment) : IInstallationPreflight
{
    public Task<InstallationPlan> PrepareAsync(InstallationRequest request, IProgress<string>? progress, CancellationToken token) => Task.Run(() =>
    {
        if (!environment.IsSupported) throw new PlatformNotSupportedException("Installation requires Windows.");
        var leases = new List<IDisposable>();
        try
        {
            token.ThrowIfCancellationRequested();
            if (request.Kind == InstallationKind.Font)
            {
                var relative = request.FontRelativePath ?? throw new InvalidDataException("Font path missing.");
                relative = relative.Replace('\\', '/');
                if (!relative.StartsWith("fonts/", StringComparison.Ordinal)) throw new InvalidDataException("Font path must be library-relative under fonts/.");
                PortablePackagePath.Normalize("packages/font/" + relative[6..]);
                var full = Path.GetFullPath(Path.Combine(paths.Root, relative));
                if (Path.IsPathRooted(relative) || !LibraryStorage.IsWithin(full, paths.Fonts) || full.Equals(paths.Fonts, StringComparison.OrdinalIgnoreCase) || relative.Split('/', '\\').Contains("..") ||
                    Path.GetExtension(full).ToLowerInvariant() is not (".ttf" or ".otf")) throw new InvalidDataException("Unsafe font path.");
                var hash = PinAndHash(full, leases, token);
                if (!hash.Equals(request.ExpectedFontHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Font content changed since selection.");
                return new InstallationPlan { Request = request, Name = Path.GetFileName(full), FilePath = full, WorkingDirectory = Path.GetDirectoryName(full)!, EntrySha256 = hash,
                    Fingerprint = Digest(request.Id + full + hash), Leases = leases };
            }
            if (request.Kind != InstallationKind.Software) throw new InvalidDataException("Unknown installation kind.");
            var candidates = new LibraryStorage(paths).ReadManifests().Where(item => item.Manifest.Id == request.Id).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException("Package is missing or its ID is ambiguous.");
            var manifestPath = candidates[0].Path;
            var manifestHash = PinAndHash(manifestPath, leases, token);
            var manifest = new JsonPackageManifestReader().Parse(File.ReadAllText(manifestPath)).Manifest ?? throw new InvalidDataException("Manifest validation failed.");
            if (manifest.Id != request.Id) throw new InvalidDataException("Package ID changed during preflight.");
            if (!ArchitectureCompatibility.IsCompatible(manifest.Architecture, environment.Architecture)) throw new InvalidDataException("Package architecture is incompatible with this workstation.");
            foreach (var file in manifest.Files)
            {
                progress?.Report("Verifying " + file.Path);
                var hash = PinAndHash(PortablePackagePath.Resolve(paths.Root, file.Path), leases, token);
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 mismatch: " + file.Path);
            }
            var entry = PortablePackagePath.Resolve(paths.Root, manifest.EntryPoint);
            var entryHash = manifest.Files.Single(f => PortablePackagePath.Normalize(f.Path).Equals(PortablePackagePath.Normalize(manifest.EntryPoint), StringComparison.OrdinalIgnoreCase)).Sha256;
            var arguments = new List<string>();
            var success = new[] { 0 }; var restart = new[] { 3010 };
            var manual = manifest.InstallationMode == InstallationMode.Manual;
            if (!manual)
            {
                var (profile, lease) = profiles.Read(manifest.VerifiedProfileId!); leases.Add(lease);
                if (profile.PackageId != manifest.Id || profile.Version != manifest.Version || profile.Architecture != manifest.Architecture || profile.InstallerType != manifest.InstallerType ||
                    !string.Equals(profile.EntrySha256, entryHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Verified profile does not match this exact package/version/architecture/hash.");
                arguments.AddRange(profile.Arguments); success = profile.SuccessExitCodes; restart = profile.RestartExitCodes;
            }
            string executable = entry;
            if (manifest.InstallerType == InstallerType.Msi)
            {
                if (arguments.Any(a => !Regex.IsMatch(a, "^[A-Z][A-Z0-9_]*=.*$", RegexOptions.CultureInvariant) || a.StartsWith("REBOOT=", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("MSI profiles may supply public property assignments only, excluding REBOOT. NEX owns MSI switches.");
                arguments.InsertRange(0, manual ? ["/i", entry] : ["/i", entry, "/qn"]);
                arguments.Add("/norestart"); arguments.Add("REBOOT=ReallySuppress");
                executable = environment.MsiExecPath;
            }
            else if (arguments.Any(a => a.Equals("/forcerestart", StringComparison.OrdinalIgnoreCase) || a.Equals("/promptrestart", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Restart-requesting arguments are not permitted.");
            return new InstallationPlan { Request = request, Name = manifest.Name + " " + manifest.Version, FilePath = executable,
                WorkingDirectory = Path.GetDirectoryName(entry)!, Arguments = arguments.AsReadOnly(), RequiresElevation = manifest.RequiresAdministrator && !environment.IsAdministrator,
                Manual = manual, InstallerType = manifest.InstallerType, EntrySha256 = entryHash, SuccessCodes = success, RestartCodes = restart,
                Fingerprint = Digest(manifestHash + JsonSerializer.Serialize(new { executable, arguments, success, restart, manifest.RequiresAdministrator })), Leases = leases };
        }
        catch { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); throw; }
    }, token);

    private static string PinAndHash(string path, List<IDisposable> leases, CancellationToken token)
    {
        LibraryStorage.CheckPath(path);
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); leases.Add(stream);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[1024 * 1024]; int count;
        while ((count = stream.Read(bytes)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(bytes, 0, count); }
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
