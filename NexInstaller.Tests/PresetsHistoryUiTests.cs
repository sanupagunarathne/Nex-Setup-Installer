using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.History;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Core.Packages;
using NexInstaller.Core.Presets;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.History;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Installation;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;
using NexInstaller.Infrastructure.Presets;
namespace NexInstaller.Tests;
public sealed class PresetsHistoryUiTests
{
    private sealed class FakeEngine : IInstallationEngine
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<InstallationResult>> RunAsync(IReadOnlyList<InstallationRequest> requests, bool continueOnFailure, IProgress<InstallationProgress>? progress = null, CancellationToken token = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<InstallationResult>>(requests.Select(request => new InstallationResult(request.Id, InstallationOutcome.Succeeded, 0, false, "Fake completion; never executed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray());
        }
    }
    private sealed class Dialogs : IPresetsHistoryDialogs
    {
        public bool AllowDiscard { get; set; } = true;
        public bool AllowReplace { get; set; } = true;
        public string? Destination { get; set; }
        public bool DiscardPresetEdits() => AllowDiscard;
        public bool ReplacePendingQueue() => AllowReplace;
        public string? SaveReport(HistoryExportFormat format) => Destination;
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
    private static ShellViewModel Shell(string root, FakeEngine engine, Dialogs? dialogs = null)
    {
        var paths = new LibraryPaths(root, "."); var catalog = new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64);
        return new(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null), new Log(), catalog,
            fontImports: new FontImportService(new LibraryStorage(paths)), installationEngine: engine, presetService: new JsonPresetService(paths, catalog), historyService: new JsonHistoryService(root, root), presetsDialogs: dialogs ?? new Dialogs());
    }
    [Fact]
    public async Task EditorCreatesRenamesAndRemovesPresetItems()
    {
        using var root = new TemporaryDirectory(); using var shell = Shell(root.Root, new FakeEngine());
        await shell.RefreshAsync(); shell.Presets.PresetName = "Office";
        shell.Presets.Add(new() { Kind = InstallationKind.Software, PackageId = "editor" });
        await shell.Presets.SaveAsync(); Assert.Equal("1", shell.PresetCount); Assert.False(shell.Presets.IsDirty);
        shell.Presets.PresetName = "Renamed"; shell.Presets.Items[0].RemoveCommand.Execute(null);
        await shell.Presets.SaveAsync(); Assert.Equal("Renamed", Assert.Single(shell.Presets.Presets).Name); Assert.Empty(shell.Presets.Items);
    }
    [Fact]
    public async Task MissingPresetReferenceNeverChangesOrStartsQueue()
    {
        using var root = new TemporaryDirectory(); var engine = new FakeEngine(); using var shell = Shell(root.Root, engine);
        shell.Presets.Add(new() { Kind = InstallationKind.Software, PackageId = "missing" }); await shell.Presets.SaveAsync();
        await shell.Presets.StartAsync(); Assert.Equal(0, engine.Calls); Assert.Equal(0, shell.InstallationQueue.Count);
        Assert.Contains("unavailable", shell.Presets.Status);
    }
    [Fact]
    public async Task ValidSoftwarePresetUsesSharedEngineAndPreservesDirtyDraftOnRefresh()
    {
        using var root = new TemporaryDirectory(); root.Write("packages/editor/setup.exe", "dummy"); root.Write("manifests/editor.json", ManifestTests.ValidJson());
        var engine = new FakeEngine(); using var shell = Shell(root.Root, engine);
        await shell.RefreshAsync(); shell.SoftwareLibrary.SelectVisibleCommand.Execute(null); shell.Presets.AddSoftwareCommand.Execute(null);
        await shell.Presets.SaveAsync(); shell.Presets.PresetName = "Unsaved name";
        await shell.RefreshAsync(); Assert.Equal("Unsaved name", shell.Presets.PresetName); Assert.True(shell.Presets.IsDirty);
        await shell.Presets.StartAsync(); Assert.Equal(0, engine.Calls);
        await shell.Presets.SaveAsync(); await shell.Presets.StartAsync(); Assert.Equal(1, engine.Calls);
    }
    [Fact]
    public async Task FontPresetRequiresNewLicenseAcknowledgementBeforeStart()
    {
        using var root = new TemporaryDirectory(); var file = root.Write("fonts/example.ttf", "dummy"); var engine = new FakeEngine(); using var shell = Shell(root.Root, engine);
        shell.Presets.Add(new() { Kind = InstallationKind.Font, FontRelativePath = "fonts/example.ttf", Sha256 = LibraryStorage.Hash(file, default) });
        await shell.Presets.SaveAsync(); await shell.Presets.StartAsync(); Assert.Equal(0, engine.Calls);
        shell.Presets.FontLicenseConfirmed = true; await shell.Presets.StartAsync(); Assert.Equal(1, engine.Calls);
    }
    [Fact]
    public async Task DecliningQueueReplacementLeavesPendingItemsUntouched()
    {
        using var root = new TemporaryDirectory(); root.Write("packages/editor/setup.exe", "dummy"); root.Write("manifests/editor.json", ManifestTests.ValidJson());
        var engine = new FakeEngine(); var dialogs = new Dialogs { AllowReplace = false }; using var shell = Shell(root.Root, engine, dialogs);
        await shell.RefreshAsync(); shell.SoftwareLibrary.SelectVisibleCommand.Execute(null);
        shell.InstallationQueue.AddSoftware(shell.SoftwareLibrary.SelectedPackages); var pending = shell.InstallationQueue.Items[0];
        shell.Presets.AddSoftwareCommand.Execute(null); await shell.Presets.SaveAsync(); await shell.Presets.StartAsync();
        Assert.Equal(0, engine.Calls); Assert.Same(pending, Assert.Single(shell.InstallationQueue.Items)); Assert.Contains("retained", shell.Presets.Status);
    }
    [Fact]
    public async Task UnsavedEditsSurviveDeclinedSelectionChange()
    {
        using var root = new TemporaryDirectory(); var dialogs = new Dialogs(); using var shell = Shell(root.Root, new FakeEngine(), dialogs);
        shell.Presets.PresetName = "First"; await shell.Presets.SaveAsync(); var first = shell.Presets.SelectedPreset!;
        shell.Presets.NewCommand.Execute(null); shell.Presets.PresetName = "Second"; await shell.Presets.SaveAsync(); var secondId = shell.Presets.SelectedPreset!.Id;
        shell.Presets.PresetName = "Unsaved"; dialogs.AllowDiscard = false; shell.Presets.SelectedPreset = first;
        Assert.Equal(secondId, shell.Presets.SelectedPreset!.Id); Assert.Equal("Unsaved", shell.Presets.PresetName);
    }
    [Fact]
    public async Task HistorySearchExportAndDashboardUseRecordedSessions()
    {
        using var root = new TemporaryDirectory(); var journal = new JsonInstallationJournal(root.Root, root.Root); var id = Guid.NewGuid().ToString("N");
        journal.Write(id, "session.start", new { count = 1 });
        journal.Write(id, "item.result", new InstallationResult("editor", InstallationOutcome.Failed, 1603, false, "Failure fixture", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        journal.Write(id, "session.complete", new { completed = 1, requested = 1, restartRequired = false });
        var dialogs = new Dialogs { Destination = Path.Combine(root.Root, "export.json") }; using var shell = Shell(root.Root, new FakeEngine(), dialogs);
        await shell.RefreshAsync(); Assert.Equal("1", shell.SessionCount);
        shell.History.Search = "not present"; Assert.Empty(shell.History.Sessions);
        shell.History.Search = "1603"; Assert.Single(shell.History.Sessions);
        shell.History.Search = "Failed"; Assert.Single(shell.History.Sessions); Assert.Contains("1603", shell.History.Details);
        await shell.History.ExportAsync(HistoryExportFormat.Json); Assert.True(File.Exists(dialogs.Destination));
    }
}
