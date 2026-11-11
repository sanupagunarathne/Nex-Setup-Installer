namespace NexInstaller.Core.Library;

public sealed record LibrarySnapshot(
    int ManifestCount,
    int FontFileCount,
    int PresetFileCount,
    int SessionReportCount,
    IReadOnlyList<string> Warnings);

public interface ILibrarySnapshotService
{
    Task<LibrarySnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
