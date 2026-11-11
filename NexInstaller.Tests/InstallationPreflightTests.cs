using System.Text.Json;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Installation;
namespace NexInstaller.Tests;
public sealed class InstallationPreflightTests
{
    internal sealed class Environment : IInstallationEnvironment
    {
        public bool IsSupported => true;
        public bool IsAdministrator => false;
        public PackageArchitecture Architecture => PackageArchitecture.X64;
        public string MsiExecPath => "trusted-system-msiexec.exe";
    }
    [Fact]
    public async Task ManualExeUsesNoArgumentsAndDetectsTampering()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var imported = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "test")) with { RequiresAdministrator = true });
        var service = new InstallationPreflight(new LibraryPaths(library.Root, "."), new VerifiedProfiles(Path.Combine(library.Root, "profiles.json")), new Environment());
        var request = new InstallationRequest(InstallationKind.Software, imported.Manifest!.Id);
        using (var plan = await service.PrepareAsync(request, null, default)) { Assert.Empty(plan.Arguments); Assert.True(plan.RequiresElevation); }
        File.WriteAllText(Path.Combine(library.Root, imported.Manifest.EntryPoint), "modified");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(request, null, default));
    }
    [Fact]
    public async Task ManualMsiIsInteractiveAndSuppressesRestart()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var imported = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.msi", "test")));
        using var plan = await new InstallationPreflight(new LibraryPaths(library.Root, "."), new VerifiedProfiles("unused"), new Environment()).PrepareAsync(new(InstallationKind.Software, imported.Manifest!.Id), null, default);
        Assert.Contains("/norestart", plan.Arguments); Assert.Contains("REBOOT=ReallySuppress", plan.Arguments); Assert.DoesNotContain("/qn", plan.Arguments);
        Assert.Equal("trusted-system-msiexec.exe", plan.FilePath);
    }
    [Fact]
    public async Task UnattendedProfileMustMatchExactPackageHashAndVerification()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var imported = await PackageImportTests.Service(library.Root).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "test")));
        var manifest = imported.Manifest! with { InstallationMode = InstallationMode.VerifiedUnattended, VerifiedProfileId = "tested-profile" };
        File.WriteAllText(Path.Combine(library.Root, "manifests", manifest.Id + ".json"), JsonSerializer.Serialize(manifest, LibraryStorage.Json));
        var profile = new VerifiedInstallationProfile { Id = "tested-profile", PackageId = manifest.Id, Version = manifest.Version,
            Architecture = manifest.Architecture, InstallerType = manifest.InstallerType, EntrySha256 = manifest.Files[0].Sha256,
            Arguments = ["vendor-documented-example"], NoRestartVerified = true, VerificationNote = "Fixture only; fake executor is always used.", VerifiedAtUtc = DateTimeOffset.UtcNow };
        var profilePath = library.Write("profiles.json", JsonSerializer.Serialize(new[] { profile }, LibraryStorage.Json));
        var service = new InstallationPreflight(new LibraryPaths(library.Root, "."), new VerifiedProfiles(profilePath), new Environment());
        using (var plan = await service.PrepareAsync(new(InstallationKind.Software, manifest.Id), null, default)) Assert.Equal(profile.Arguments, plan.Arguments);
        File.WriteAllText(profilePath, JsonSerializer.Serialize(new[] { profile with { EntrySha256 = new string('0', 64) } }, LibraryStorage.Json));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(new(InstallationKind.Software, manifest.Id), null, default));
        File.WriteAllText(profilePath, JsonSerializer.Serialize(new[] { profile with { NoRestartVerified = false } }, LibraryStorage.Json));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(new(InstallationKind.Software, manifest.Id), null, default));
    }
}
