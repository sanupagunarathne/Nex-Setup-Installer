using NexInstaller.Core.Configuration;
using NexInstaller.Core.Library;

namespace NexInstaller.Infrastructure.Library;

/// <summary>Read-only foundation inventory. Does not parse manifests or execute packages.</summary>
public sealed class FileSystemLibrarySnapshotService(LibraryPaths paths) : ILibrarySnapshotService
{
    public Task<LibrarySnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private LibrarySnapshot Read(CancellationToken token)
    {
        var warnings = new List<string>();
        var manifests = Count(paths.Manifests, false, [".json"], warnings, token);
        var fonts = Count(paths.Fonts, true, [".ttf", ".otf"], warnings, token);
        var presets = Count(paths.Presets, false, [".json"], warnings, token);
        var history = Count(paths.History, false, [".json"], warnings, token);
        return new(manifests, fonts, presets, history, warnings.AsReadOnly());
    }

    private static int Count(string directory, bool recursive, string[] extensions, List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            // GetAttributes distinguishes missing folders from inaccessible folders and skips links.
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                warnings.Add($"Linked library folder was skipped: {directory}");
                return 0;
            }
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                token.ThrowIfCancellationRequested();
                if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            warnings.Add($"Library folder is missing: {directory}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Library folder could not be fully read: {directory}. Count unavailable.");
            return 0;
        }
    }
}
