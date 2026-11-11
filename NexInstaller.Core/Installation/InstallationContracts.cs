using NexInstaller.Core.Packages;
namespace NexInstaller.Core.Installation;

public enum InstallationKind { Software, Font }
public enum InstallationOutcome { Succeeded, Failed, Declined, Cancelled, RestartInitiated }
public sealed record InstallationRequest(InstallationKind Kind, string Id, string? FontRelativePath = null, string? ExpectedFontHash = null);
public sealed record InstallationResult(string Id, InstallationOutcome Outcome, int? ExitCode, bool RestartRequired, string Message, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc);
public sealed record InstallationProgress(int Completed, int Total, string Stage);
public sealed class InstallationPlan : IDisposable
{
    public required InstallationRequest Request { get; init; }
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string Fingerprint { get; init; }
    public string? EntrySha256 { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public bool RequiresElevation { get; init; }
    public bool Manual { get; init; }
    public InstallerType? InstallerType { get; init; }
    public IReadOnlyList<int> SuccessCodes { get; init; } = [0];
    public IReadOnlyList<int> RestartCodes { get; init; } = [3010];
    public IReadOnlyList<IDisposable> Leases { get; init; } = [];
    public void Dispose() { foreach (var lease in Leases.Reverse()) lease.Dispose(); }
}
public interface IInstallationPreflight
{
    Task<InstallationPlan> PrepareAsync(InstallationRequest request, IProgress<string>? progress, CancellationToken token);
}
public interface IInstallationExecutor
{
    // Cancellation is intentionally excluded: do not kill or abandon a running installer.
    Task<int> ExecuteAsync(InstallationPlan plan);
}
public interface IInstallationInteraction
{
    Task<bool> ConfirmAsync(InstallationPlan plan);
    Task<bool> ConfirmManualCompletionAsync(InstallationPlan plan, int exitCode);
}
public interface IInstallationJournal
{
    // Must durably record before execution. Failure blocks launching the next item.
    void Write(string sessionId, string eventName, object details);
}
public interface IInstallationEngine
{
    Task<IReadOnlyList<InstallationResult>> RunAsync(IReadOnlyList<InstallationRequest> requests, bool continueOnFailure,
        IProgress<InstallationProgress>? progress = null, CancellationToken token = default);
}
