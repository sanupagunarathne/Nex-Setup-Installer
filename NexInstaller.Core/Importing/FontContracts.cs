namespace NexInstaller.Core.Importing;
public sealed record FontRecord(string Name, string Path, string Sha256);
public sealed record FontImportResult(string Source, string Status, string Message);
public interface IFontImportService
{
    Task<IReadOnlyList<FontImportResult>> ImportAsync(IReadOnlyList<string> sources, bool licenseConfirmed, IProgress<string>? progress = null, CancellationToken token = default);
    Task<IReadOnlyList<FontRecord>> ListAsync(CancellationToken token = default);
}
