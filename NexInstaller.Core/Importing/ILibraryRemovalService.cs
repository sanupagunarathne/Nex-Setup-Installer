namespace NexInstaller.Core.Importing;

public interface ILibraryRemovalService
{
    Task RemovePackagesAsync(IReadOnlyList<string> packageIds, CancellationToken token = default);
    Task RemoveFontAsync(string path, CancellationToken token = default);
    Task RemovePresetAsync(string id, CancellationToken token = default);
}
