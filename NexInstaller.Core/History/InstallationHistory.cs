using NexInstaller.Core.Installation;
namespace NexInstaller.Core.History;
public enum HistorySessionState { Finished, Incomplete, Inconsistent }
public enum HistoryExportFormat { Json, Csv }
public sealed record HistoryItem(string Id, string? Name, InstallationKind? Kind, InstallationOutcome? Outcome, int? ExitCode,
    bool RestartRequired, DateTimeOffset StartedUtc, DateTimeOffset? FinishedUtc, string Message);
public sealed record HistorySession(string Id, DateTimeOffset StartedUtc, DateTimeOffset? FinishedUtc, int? RequestedCount,
    HistorySessionState State, IReadOnlyList<HistoryItem> Items, IReadOnlyList<string> Problems, IReadOnlyList<string> Sources)
{
    public bool RestartRequired => Items.Any(item => item.RestartRequired);
    public int FailureCount => Items.Count(item => item.Outcome is InstallationOutcome.Failed or InstallationOutcome.RestartInitiated);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => $"{StartedUtc.LocalDateTime:g} · {State} · {Items.Count} recorded items";
}
public sealed record HistoryInventory(IReadOnlyList<HistorySession> Sessions, IReadOnlyList<string> Warnings);
public interface IHistoryService
{
    Task<HistoryInventory> ReadAsync(CancellationToken token = default);
    Task ExportAsync(HistorySession session, HistoryExportFormat format, string destination, IReadOnlyList<string> warnings,
        bool overwrite = false, CancellationToken token = default);
}
