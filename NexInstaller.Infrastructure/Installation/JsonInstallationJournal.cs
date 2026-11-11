using System.Text.Json;
using System.Text.Json.Serialization;
using NexInstaller.Core.Installation;
namespace NexInstaller.Infrastructure.Installation;

public sealed class JsonInstallationJournal(string applicationDirectory, string localDataDirectory) : IInstallationJournal
{
    private readonly object gate = new();
    private readonly string[] directories = [Path.Combine(applicationDirectory, "logs"), Path.Combine(localDataDirectory, "NexInstaller", "logs")];
    public void Write(string sessionId, string eventName, object details)
    {
        var line = JsonSerializer.Serialize(new { eventId = Guid.NewGuid().ToString("N"), timestampUtc = DateTimeOffset.UtcNow, sessionId, eventName, details },
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) + "\n";
        lock (gate)
        {
            foreach (var directory in directories)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    using var stream = new FileStream(Path.Combine(directory, $"installations-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(line); stream.Write(bytes); stream.Flush(true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            throw new IOException("Installation logging is unavailable in both local locations. Queue stopped; do not blindly retry any item that may already have completed.");
        }
    }
}
