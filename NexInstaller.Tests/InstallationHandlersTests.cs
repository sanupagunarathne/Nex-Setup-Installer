using System.Security.Cryptography;
using System.Text.Json;
using NexInstaller.Core.Installation;
using NexInstaller.Infrastructure.Installation;
namespace NexInstaller.Tests;
public sealed class InstallationHandlersTests
{
    private sealed class FontApi : IUserFontApi
    {
        public bool Reject { get; set; }
        public string? Existing { get; set; }
        public bool Registered { get; private set; }
        public string GetRegistrationName(string file) => "Test face";
        public string? GetRegisteredPath(string name) => Existing;
        public bool AddResource(string file) => !Reject;
        public void RemoveResource(string file) { }
        public void Register(string name, string file) => Registered = true;
        public void NotifyChanged() { }
    }
    private static InstallationPlan FontPlan(string path) => new() { Request = new(InstallationKind.Font, "font"), Name = "Test", FilePath = path, WorkingDirectory = Path.GetDirectoryName(path)!, Fingerprint = "test", EntrySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    [Fact]
    public void FontCopyIsVerifiedAndRegistrationUsesFakeApiOnly()
    {
        using var source = new TemporaryDirectory(); using var target = new TemporaryDirectory();
        using var plan = FontPlan(source.Write("test.ttf", "dummy bytes never given to Windows")); var api = new FontApi();
        Assert.Equal(0, new UserFontInstaller(target.Root, api).Install(plan));
        Assert.True(api.Registered); Assert.Single(Directory.GetFiles(target.Root, "*.ttf"));
    }
    [Fact]
    public void NativeRejectionRollsBackNewFontFile()
    {
        using var source = new TemporaryDirectory(); using var target = new TemporaryDirectory();
        using var plan = FontPlan(source.Write("test.otf", "dummy"));
        Assert.Throws<IOException>(() => new UserFontInstaller(target.Root, new FontApi { Reject = true }).Install(plan));
        Assert.Empty(Directory.GetFiles(target.Root));
    }
    [Fact]
    public void ExistingFaceIsNeverOverwritten()
    {
        using var source = new TemporaryDirectory(); using var target = new TemporaryDirectory();
        using var plan = FontPlan(source.Write("test.ttf", "dummy"));
        Assert.Throws<IOException>(() => new UserFontInstaller(target.Root, new FontApi { Existing = "other-font.ttf" }).Install(plan));
        Assert.Empty(Directory.GetFiles(target.Root));
    }
    [Fact]
    public void JournalFallsBackAndStoresStructuredExitCode()
    {
        using var primary = new TemporaryDirectory(); using var fallback = new TemporaryDirectory(); primary.Write("logs", "blocked");
        new JsonInstallationJournal(primary.Root, fallback.Root).Write("session", "item.result", new { ExitCode = 3010, RestartRequired = true });
        var file = Assert.Single(Directory.GetFiles(fallback.Root, "*.jsonl", SearchOption.AllDirectories));
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(3010, json.RootElement.GetProperty("details").GetProperty("ExitCode").GetInt32());
    }
    [Fact]
    public void JournalReportsBothLocationsBlocked()
    {
        using var primary = new TemporaryDirectory(); primary.Write("logs", "blocked"); primary.Write("NexInstaller", "blocked");
        Assert.Throws<IOException>(() => new JsonInstallationJournal(primary.Root, primary.Root).Write("session", "start", new { }));
    }
}
