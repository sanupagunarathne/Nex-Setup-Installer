using NexInstaller.Core.Installation;
namespace NexInstaller.Tests;
public sealed class InstallationEngineTests
{
    private sealed class Preflight : IInstallationPreflight
    {
        public bool Fail { get; set; }
        public bool ChangeAfterConfirmation { get; set; }
        private int calls;
        public Task<InstallationPlan> PrepareAsync(InstallationRequest request, IProgress<string>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidDataException("hash mismatch");
            return Task.FromResult(new InstallationPlan { Request = request, Name = request.Id, FilePath = "never-executed.exe", WorkingDirectory = ".", Fingerprint = request.Id + (ChangeAfterConfirmation ? ++calls : 0) });
        }
    }
    private sealed class Executor(params int[] codes) : IInstallationExecutor
    {
        public int Calls { get; private set; }
        public Action? OnExecute { get; set; }
        public Task<int> ExecuteAsync(InstallationPlan plan) { OnExecute?.Invoke(); return Task.FromResult(codes[Calls++]); }
    }
    private sealed class Interaction : IInstallationInteraction
    {
        public bool Confirm { get; set; } = true;
        public Task<bool> ConfirmAsync(InstallationPlan plan) => Task.FromResult(Confirm);
        public Task<bool> ConfirmManualCompletionAsync(InstallationPlan plan, int exitCode) => Task.FromResult(true);
    }
    private sealed class Journal : IInstallationJournal
    {
        public bool Fail { get; set; }
        public List<string> Events { get; } = [];
        public void Write(string sessionId, string eventName, object details) { if (Fail) throw new IOException("Logging unavailable"); Events.Add(eventName); }
    }
    private static InstallationRequest[] Queue() => [new(InstallationKind.Software, "one"), new(InstallationKind.Software, "two")];
    [Fact]
    public async Task SequentialQueueReportsExitCodesAndRestartRequirement()
    {
        var executor = new Executor(0, 3010); var journal = new Journal();
        var results = await new InstallationEngine(new Preflight(), executor, new Interaction(), journal).RunAsync(Queue(), false);
        Assert.Equal(2, executor.Calls); Assert.Equal(3010, results[1].ExitCode); Assert.True(results[1].RestartRequired);
        Assert.All(results, result => Assert.Equal(InstallationOutcome.Succeeded, result.Outcome));
        Assert.Equal(2, journal.Events.Count(e => e == "item.result"));
    }
    [Theory]
    [InlineData(false, 1)][InlineData(true, 2)]
    public async Task FailurePolicyControlsContinuation(bool continueOnFailure, int expected)
    {
        var executor = new Executor(1603, 0);
        var results = await new InstallationEngine(new Preflight(), executor, new Interaction(), new Journal()).RunAsync(Queue(), continueOnFailure);
        Assert.Equal(expected, executor.Calls); Assert.Equal(InstallationOutcome.Failed, results[0].Outcome);
    }
    [Fact]
    public async Task DecliningOrFailedPreflightNeverLaunches()
    {
        var executor = new Executor(0); var preflight = new Preflight(); var interaction = new Interaction { Confirm = false };
        Assert.Equal(InstallationOutcome.Declined, Assert.Single(await new InstallationEngine(preflight, executor, interaction, new Journal()).RunAsync(Queue(), true)).Outcome);
        preflight.Fail = true;
        Assert.Equal(InstallationOutcome.Failed, Assert.Single(await new InstallationEngine(preflight, executor, interaction, new Journal()).RunAsync(Queue(), false)).Outcome);
        Assert.Equal(0, executor.Calls);
    }
    [Fact]
    public async Task CancellationDuringExecutionWaitsAndStopsBeforeNextItem()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new Executor(0) { OnExecute = cancellation.Cancel };
        var results = await new InstallationEngine(new Preflight(), executor, new Interaction(), new Journal()).RunAsync(Queue(), true, token: cancellation.Token);
        Assert.Equal(1, executor.Calls); Assert.Equal(InstallationOutcome.Succeeded, Assert.Single(results).Outcome);
    }
    [Fact]
    public async Task InstallerInitiatedRestartAlwaysStopsQueue()
    {
        var executor = new Executor(1641);
        var result = Assert.Single(await new InstallationEngine(new Preflight(), executor, new Interaction(), new Journal()).RunAsync(Queue(), true));
        Assert.Equal(InstallationOutcome.RestartInitiated, result.Outcome); Assert.True(result.RestartRequired);
    }
    [Fact]
    public async Task ChangedPlanAfterConfirmationNeverExecutes()
    {
        var executor = new Executor(0);
        var result = Assert.Single(await new InstallationEngine(new Preflight { ChangeAfterConfirmation = true }, executor, new Interaction(), new Journal()).RunAsync(Queue(), false));
        Assert.Equal(InstallationOutcome.Failed, result.Outcome); Assert.Equal(0, executor.Calls);
    }
    [Fact]
    public async Task MissingJournalBlocksExecution()
    {
        var executor = new Executor(0);
        await Assert.ThrowsAsync<IOException>(() => new InstallationEngine(new Preflight(), executor, new Interaction(), new Journal { Fail = true }).RunAsync(Queue(), false));
        Assert.Equal(0, executor.Calls);
    }
}
