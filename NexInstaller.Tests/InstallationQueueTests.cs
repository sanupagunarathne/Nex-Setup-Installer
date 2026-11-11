using NexInstaller.App.ViewModels;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Tests;
public sealed class InstallationQueueTests
{
    private static PackageRowViewModel Row()
    {
        var manifest = new JsonPackageManifestReader().Parse(ManifestTests.ValidJson()).Manifest!;
        return new(new CatalogPackage(manifest, "manifests/editor.json", 10, []), () => { });
    }
    private sealed class FakeEngine : IInstallationEngine
    {
        public bool Delay { get; set; }
        public TaskCompletionSource<IReadOnlyList<InstallationResult>> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<InstallationResult>> RunAsync(IReadOnlyList<InstallationRequest> requests, bool continueOnFailure, IProgress<InstallationProgress>? progress = null, CancellationToken token = default)
        {
            if (Delay) return Pending.Task;
            progress?.Report(new(1, 1, "Old progress must not replace final summary"));
            return Task.FromResult<IReadOnlyList<InstallationResult>>([new(requests[0].Id, InstallationOutcome.Succeeded, 3010, true, "Restart required", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        }
    }
    [Fact]
    public async Task SoftwareQueueDeduplicatesAndReportsRestart()
    {
        var vm = new InstallationQueueViewModel(new FakeEngine());
        vm.AddSoftware([Row(), Row()]); Assert.Equal(1, vm.Count);
        await vm.StartAsync();
        Assert.True(Assert.Single(vm.Results).RestartRequired); Assert.Equal(100, vm.Percent);
        Assert.Contains("Restart required", vm.Status); Assert.False(vm.IsBusy);
    }
    [Fact]
    public void FontQueueRequiresLicenseAcknowledgement()
    {
        var vm = new InstallationQueueViewModel(new FakeEngine());
        var root = Path.GetTempPath(); var font = new FontRecord("font.ttf", Path.Combine(root, "fonts", "font.ttf"), new string('a', 64));
        vm.AddFont(font, root, false); Assert.Equal(0, vm.Count);
        vm.AddFont(font, root, true); Assert.Equal(1, vm.Count);
        Assert.Equal(InstallationKind.Font, Assert.Single(vm.Items).Request.Kind);
    }
    [Fact]
    public async Task BusyQueueCannotBeChangedOrCleared()
    {
        var engine = new FakeEngine { Delay = true }; var vm = new InstallationQueueViewModel(engine);
        vm.AddSoftware([Row()]); var running = vm.StartAsync();
        vm.ClearCommand.Execute(null); Assert.Equal(1, vm.Count); Assert.True(vm.IsBusy);
        vm.Items[0].RemoveCommand.Execute(null); Assert.Equal(1, vm.Count);
        engine.Pending.SetResult([]); await running;
        Assert.False(vm.IsBusy); Assert.Equal("Not started", vm.Items[0].Status);
    }
}
