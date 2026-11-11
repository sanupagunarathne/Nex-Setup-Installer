using System.Text.Json;
using NexInstaller.Infrastructure.Logging;

namespace NexInstaller.Tests;

public sealed class LocalLogTests
{
    [Fact]
    public void WritesStructuredJsonWithoutExceptionDetails()
    {
        using var directory = new TemporaryDirectory();
        var log = new JsonLocalLog(directory.Root, directory.Root);
        Assert.True(log.TryWrite("test.event", "message", new IOException("private details")));
        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(log.LogDirectory!, "*.jsonl")));
        using var json = JsonDocument.Parse(text);
        Assert.Equal("test.event", json.RootElement.GetProperty("eventName").GetString());
        Assert.True(json.RootElement.TryGetProperty("timestampUtc", out _));
        Assert.DoesNotContain("private details", text);
    }

    [Fact]
    public void FallsBackWhenApplicationLogLocationCannotBeWritten()
    {
        using var directory = new TemporaryDirectory();
        // A regular file blocks directory creation on both Linux and Windows, even for privileged users.
        directory.Write("logs", "blocked");
        var fallback = Path.Combine(directory.Root, "local");
        var log = new JsonLocalLog(directory.Root, fallback);
        Assert.True(log.TryWrite("test.fallback", "message"));
        Assert.Equal(Path.Combine(fallback, "NexInstaller", "logs"), log.LogDirectory);
    }

    [Fact]
    public void ReturnsFailureWhenBothLocationsAreBlocked()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("logs", "blocked");
        directory.Write("NexInstaller", "blocked");
        var log = new JsonLocalLog(directory.Root, directory.Root);
        Assert.False(log.TryWrite("test.failure", "message"));
        Assert.Null(log.LogDirectory);
    }
}
