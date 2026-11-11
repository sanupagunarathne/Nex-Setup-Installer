namespace NexInstaller.Core.Installation;

public sealed class InstallationEngine(IInstallationPreflight preflight, IInstallationExecutor executor,
    IInstallationInteraction interaction, IInstallationJournal journal) : IInstallationEngine
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IReadOnlyList<InstallationResult>> RunAsync(IReadOnlyList<InstallationRequest> requests, bool continueOnFailure,
        IProgress<InstallationProgress>? progress = null, CancellationToken token = default)
    {
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException("An installation queue is already running.");
        var results = new List<InstallationResult>();
        var session = Guid.NewGuid().ToString("N");
        try
        {
            var queue = requests.ToArray();
            if (queue.Length == 0) return results;
            if (queue.Length > 1000 || queue.Distinct().Count() != queue.Length) throw new ArgumentException("Queue must contain up to 1000 distinct items.");
            journal.Write(session, "session.start", new { count = queue.Length, continueOnFailure });
            foreach (var request in queue)
            {
                var started = DateTimeOffset.UtcNow;
                InstallationResult result;
                try
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new(results.Count, queue.Length, "Preflight: " + request.Id));
                    using var plan = await preflight.PrepareAsync(request, null, token);
                    if (!await interaction.ConfirmAsync(plan))
                        result = new(request.Id, InstallationOutcome.Declined, null, false, "Technician declined. Nothing was executed.", started, DateTimeOffset.UtcNow);
                    else
                    {
                        token.ThrowIfCancellationRequested();
                        // Re-read metadata and hashes after the confirmation dialog. The first plan keeps its files pinned too.
                        using var current = await preflight.PrepareAsync(request, null, token);
                        if (plan.Fingerprint != current.Fingerprint) throw new InvalidDataException("Installation input changed after confirmation. Review and queue it again.");
                        journal.Write(session, "item.start", new { request.Kind, request.Id, plan.Name, plan.Fingerprint, plan.RequiresElevation, plan.Manual });
                        progress?.Report(new(results.Count, queue.Length, "Installing " + plan.Name + ". Stop queue waits for this item to finish."));
                        var code = await executor.ExecuteAsync(current);
                        var restart = plan.RestartCodes.Contains(code) || code == 1641;
                        var outcome = code == 1641 ? InstallationOutcome.RestartInitiated :
                            plan.SuccessCodes.Contains(code) || plan.RestartCodes.Contains(code) ? InstallationOutcome.Succeeded : InstallationOutcome.Failed;
                        var message = code == 1641 ? "Installer reported a restart was initiated. NEX did not request a restart; queue stopped." :
                            outcome == InstallationOutcome.Succeeded ? restart ? "Completed. Windows restart required; NEX will not restart it." : "Completed." : $"Installer failed with exit code {code}.";
                        if (plan.Manual && plan.InstallerType == Packages.InstallerType.Exe && outcome == InstallationOutcome.Succeeded &&
                            !await interaction.ConfirmManualCompletionAsync(plan, code))
                        { outcome = InstallationOutcome.Failed; message = "Technician did not confirm completion. Installer may have spawned a child or been cancelled."; }
                        result = new(request.Id, outcome, code, restart, message, started, DateTimeOffset.UtcNow);
                    }
                }
                catch (OperationCanceledException)
                { result = new(request.Id, InstallationOutcome.Cancelled, null, false, "Cancelled before launching this item.", started, DateTimeOffset.UtcNow); }
                catch (Exception ex)
                { result = new(request.Id, InstallationOutcome.Failed, null, false, ex.Message, started, DateTimeOffset.UtcNow); }
                results.Add(result);
                journal.Write(session, "item.result", result);
                progress?.Report(new(results.Count, queue.Length, result.Id + ": " + result.Message));
                if (token.IsCancellationRequested || result.Outcome is InstallationOutcome.Declined or InstallationOutcome.Cancelled or InstallationOutcome.RestartInitiated ||
                    (!continueOnFailure && result.Outcome == InstallationOutcome.Failed)) break;
            }
            journal.Write(session, "session.complete", new { completed = results.Count, requested = queue.Length, restartRequired = results.Any(r => r.RestartRequired) });
            return results.AsReadOnly();
        }
        finally { gate.Release(); }
    }
}
