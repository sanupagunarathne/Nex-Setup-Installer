using System.Text.Json;
using NexInstaller.Core.Logging;

namespace NexInstaller.Infrastructure.Logging;

public sealed class JsonLocalLog : ILocalLog
{
    private readonly string[] candidates;
    private readonly object gate = new();
    public string? LogDirectory { get; private set; }

    public JsonLocalLog(string applicationDirectory, string localDataDirectory)
    {
        candidates = [Path.Combine(applicationDirectory, "logs"), Path.Combine(localDataDirectory, "NexInstaller", "logs")];
    }

    public bool TryWrite(string eventName, string message, Exception? exception = null)
    {
        lock (gate)
        {
            foreach (var directory in candidates)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    var entry = JsonSerializer.Serialize(new
                    {
                        timestampUtc = DateTimeOffset.UtcNow,
                        eventName,
                        message,
                        exceptionType = exception?.GetType().FullName
                    });
                    File.AppendAllText(Path.Combine(directory, $"nex-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"), entry + Environment.NewLine);
                    LogDirectory = directory;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // A read-only USB must not prevent the application from opening.
                }
            }
            LogDirectory = null;
            return false;
        }
    }
}
