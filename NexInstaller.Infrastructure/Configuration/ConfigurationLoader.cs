using System.Text.Json;
using NexInstaller.Core.Configuration;

namespace NexInstaller.Infrastructure.Configuration;

public sealed record LoadedConfiguration(LibraryPaths Paths, string SourcePath, string? Warning);

public static class ConfigurationLoader
{
    public static LoadedConfiguration Load(string applicationDirectory)
    {
        var source = Path.Combine(applicationDirectory, "nexinstaller.json");
        try
        {
            if (!File.Exists(source))
                return Defaults(applicationDirectory, source, "Configuration is missing. Using the application-relative package-library folder.");

            using var stream = File.OpenRead(source);
            var settings = JsonSerializer.Deserialize<Settings>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
            });
            if (settings is null || string.IsNullOrWhiteSpace(settings.LibraryRoot))
                return Defaults(applicationDirectory, source, "Configuration needs a non-empty libraryRoot. Using the default library folder.");

            return new(new LibraryPaths(applicationDirectory, settings.LibraryRoot), source, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return Defaults(applicationDirectory, source, "Configuration could not be loaded. Using the application-relative package-library folder.");
        }
    }

    private static LoadedConfiguration Defaults(string root, string source, string warning) =>
        new(new LibraryPaths(root, "package-library"), source, warning);

    private sealed record Settings(string LibraryRoot);
}
