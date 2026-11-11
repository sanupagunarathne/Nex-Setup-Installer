using System.Text.Json;
using NexInstaller.Core.History;
using NexInstaller.Core.Installation;
using NexInstaller.Infrastructure.History;
using NexInstaller.Infrastructure.Installation;
namespace NexInstaller.Tests;
public sealed class HistoryTests
{
    private static string Session() => Guid.NewGuid().ToString("N");
    private static void WriteSession(JsonInstallationJournal journal, string id, bool complete = true)
    {
        journal.Write(id, "session.start", new { count = 1, continueOnFailure = false });
        journal.Write(id, "item.start", new { Kind = InstallationKind.Software, Id = "editor", Name = "=UNTRUSTED()" });
        if (!complete) return;
        journal.Write(id, "item.result", new InstallationResult("editor", InstallationOutcome.Succeeded, 3010, true, "Restart required", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));
        journal.Write(id, "session.complete", new { completed = 1, requested = 1, restartRequired = true });
    }
    [Fact]
    public async Task ReconstructsActualOutcomesAndDeduplicatesFallbackCopies()
    {
        using var primary = new TemporaryDirectory(); using var local = new TemporaryDirectory();
        WriteSession(new(primary.Root, local.Root), Session());
        var source = Assert.Single(Directory.GetFiles(Path.Combine(primary.Root, "logs")));
        local.Write("NexInstaller/logs/" + Path.GetFileName(source), File.ReadAllText(source));
        var history = await new JsonHistoryService(primary.Root, local.Root).ReadAsync(); var session = Assert.Single(history.Sessions);
        Assert.Equal(HistorySessionState.Finished, session.State); Assert.True(session.RestartRequired); Assert.Equal(3010, Assert.Single(session.Items).ExitCode);
        Assert.Empty(history.Warnings);
    }
    [Fact]
    public async Task InterruptedSessionIsNotReportedAsSuccessful()
    {
        using var primary = new TemporaryDirectory(); WriteSession(new(primary.Root, primary.Root), Session(), false);
        var session = Assert.Single((await new JsonHistoryService(primary.Root, primary.Root).ReadAsync()).Sessions);
        Assert.Equal(HistorySessionState.Incomplete, session.State); Assert.Null(Assert.Single(session.Items).Outcome);
    }
    [Fact]
    public async Task BadLogLinesAreReportedWithoutHidingValidEvents()
    {
        using var primary = new TemporaryDirectory(); WriteSession(new(primary.Root, primary.Root), Session());
        File.AppendAllText(Assert.Single(Directory.GetFiles(Path.Combine(primary.Root, "logs"))), "{broken\n");
        var inventory = await new JsonHistoryService(primary.Root, primary.Root).ReadAsync(); Assert.Single(inventory.Sessions); Assert.NotEmpty(inventory.Warnings);
    }
    [Fact]
    public async Task ExportsJsonAndFormulaSafeCsvWithoutOverwritingByDefault()
    {
        using var primary = new TemporaryDirectory(); WriteSession(new(primary.Root, primary.Root), Session());
        var service = new JsonHistoryService(primary.Root, primary.Root); var session = Assert.Single((await service.ReadAsync()).Sessions);
        var csv = Path.Combine(primary.Root, "report.csv"); await service.ExportAsync(session, HistoryExportFormat.Csv, csv, []);
        Assert.Contains("\"'=UNTRUSTED()\"", File.ReadAllText(csv));
        await Assert.ThrowsAnyAsync<IOException>(() => service.ExportAsync(session, HistoryExportFormat.Csv, csv, []));
        var json = Path.Combine(primary.Root, "report.json"); await service.ExportAsync(session, HistoryExportFormat.Json, json, []);
        using var document = JsonDocument.Parse(File.ReadAllText(json)); Assert.Equal(session.Id, document.RootElement.GetProperty("session").GetProperty("id").GetString());
    }
    [Fact]
    public async Task MissingOutcomeCannotBecomeDefaultSuccess()
    {
        using var primary = new TemporaryDirectory(); WriteSession(new(primary.Root, primary.Root), Session());
        var log = Assert.Single(Directory.GetFiles(Path.Combine(primary.Root, "logs")));
        File.WriteAllText(log, File.ReadAllText(log).Replace("\"Outcome\":\"Succeeded\",", ""));
        var session = Assert.Single((await new JsonHistoryService(primary.Root, primary.Root).ReadAsync()).Sessions);
        Assert.Equal(HistorySessionState.Inconsistent, session.State); Assert.Null(Assert.Single(session.Items).Outcome);
    }
    [Fact]
    public async Task ConflictingEventCopyMakesHistoryInconsistent()
    {
        using var primary = new TemporaryDirectory(); using var local = new TemporaryDirectory(); WriteSession(new(primary.Root, local.Root), Session());
        var log = Assert.Single(Directory.GetFiles(Path.Combine(primary.Root, "logs")));
        local.Write("NexInstaller/logs/" + Path.GetFileName(log), File.ReadAllText(log).Replace("=UNTRUSTED()", "Different name"));
        var history = await new JsonHistoryService(primary.Root, local.Root).ReadAsync();
        Assert.Equal(HistorySessionState.Inconsistent, Assert.Single(history.Sessions).State); Assert.NotEmpty(history.Warnings);
    }
    [Fact]
    public async Task MissingLogsAndCancellationDoNotCreateFiles()
    {
        using var primary = new TemporaryDirectory(); var service = new JsonHistoryService(primary.Root, primary.Root);
        Assert.Empty((await service.ReadAsync()).Sessions);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(cancellation.Token)); Assert.Empty(Directory.GetFiles(primary.Root));
    }
}
