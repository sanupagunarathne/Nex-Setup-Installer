using NexInstaller.App.ViewModels;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Tests;
public sealed class LateProgressTests
{
    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
    private sealed class Engine : IInstallationEngine
    {
        public IProgress<InstallationProgress>? Progress { get; private set; }
        public Task<IReadOnlyList<InstallationResult>> RunAsync(IReadOnlyList<InstallationRequest> requests, bool continueOnFailure, IProgress<InstallationProgress>? progress = null, CancellationToken token = default)
        {
            Progress = progress;
            return Task.FromResult<IReadOnlyList<InstallationResult>>([new(requests[0].Id, InstallationOutcome.Succeeded, 3010, true, "Restart required", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        }
    }
    [Fact]
    public async Task LateProgressCannotReplaceFinalRestartSummaryBeforeBusyFlagClears()
    {
        var engine = new Engine(); var vm = new InstallationQueueViewModel(engine);
        var manifest = new JsonPackageManifestReader().Parse(ManifestTests.ValidJson()).Manifest!;
        vm.AddSoftware([new PackageRowViewModel(new CatalogPackage(manifest, "manifests/test.json", 1, []), () => { })]);
        var sent = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (!sent && args.PropertyName == nameof(vm.Status) && vm.Status.StartsWith("1/1 processed", StringComparison.Ordinal))
            { sent = true; engine.Progress!.Report(new(0, 1, "Late preflight message")); }
        };
        var previous = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(new InlineContext()); await vm.StartAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(sent); Assert.Contains("Restart required", vm.Status);
    }
}
