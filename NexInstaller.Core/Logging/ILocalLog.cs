namespace NexInstaller.Core.Logging;

public interface ILocalLog
{
    string? LogDirectory { get; }
    bool TryWrite(string eventName, string message, Exception? exception = null);
}
