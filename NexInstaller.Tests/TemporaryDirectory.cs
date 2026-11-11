namespace NexInstaller.Tests;

public sealed class TemporaryDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nex-tests", Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Root);
    public string Write(string relative, string contents = "")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
