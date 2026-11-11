using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using NexInstaller.Core.History;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Presets;
namespace NexInstaller.App.ViewModels;

public interface IPresetsHistoryDialogs
{
    bool DiscardPresetEdits();
    bool ReplacePendingQueue();
    string? SaveReport(HistoryExportFormat format);
}
public sealed class PresetItemViewModel(PresetItem item, Action<PresetItemViewModel> remove)
{
    public PresetItem Item { get; } = item;
    public string Label => Item.Label;
    public RelayCommand RemoveCommand => new(() => remove(this));
}
public sealed class PresetsViewModel : ImportWorkspace
{
    private readonly ShellViewModel shell;
    private readonly IPresetService? service;
    private readonly IPresetsHistoryDialogs? dialogs;
    private bool loading;
    private string id = Guid.NewGuid().ToString("N");
    private string name = "New preset";
    private bool dirty;
    private InstallationPreset? selected;
    public PresetsViewModel(ShellViewModel shell, IPresetService? service, IPresetsHistoryDialogs? dialogs)
        : base("Saved setups", "Keep your usual app and font choices ready to reuse.")
    {
        this.shell = shell; this.service = service; this.dialogs = dialogs;
        Items.CollectionChanged += (_, _) => { if (!loading) MarkDirty(); };
        NewCommand = new(() => { if (!IsBusy && (!IsDirty || dialogs?.DiscardPresetEdits() == true)) LoadDraft(null); });
        AddSoftwareCommand = new(() => { if (!IsBusy) foreach (var row in shell.SoftwareLibrary.SelectedPackages) Add(new() { Kind = InstallationKind.Software, PackageId = row.Package.Manifest.Id }); });
        AddFontCommand = new(() =>
        {
            if (IsBusy) return;
            var font = shell.FontLibrary.SelectedFont;
            if (font is null) { Status = "Select a font row in Fonts first."; return; }
            Add(new() { Kind = InstallationKind.Font, FontRelativePath = Path.GetRelativePath(shell.LibraryRoot, font.Path).Replace('\\', '/'), Sha256 = font.Sha256 });
        });
        SaveCommand = new(SaveAsync, ex => Status = ex.Message);
        StartCommand = new(StartAsync, ex => Status = ex.Message);
    }
    public bool IsLoaded { get; private set; }
    public IReadOnlyList<string> Diagnostics { get; private set; } = [];
    public ObservableCollection<InstallationPreset> Presets { get; } = [];
    public ObservableCollection<PresetItemViewModel> Items { get; } = [];
    public InstallationQueueViewModel Queue => shell.InstallationQueue;
    public bool IsDirty => dirty;
    public string EditNotice => IsDirty ? "Unsaved changes — save before starting." : selected is null ? "New draft — save to create a preset file." : "Saved presets are resolved again before execution.";
    public string PresetName { get => name; set { if (name == value) return; name = value; Raise(); if (!loading) MarkDirty(); } }
    public bool FontLicenseConfirmed { get; set; }
    public InstallationPreset? SelectedPreset
    {
        get => selected;
        set
        {
            if (loading || ReferenceEquals(value, selected)) return;
            if (IsBusy || (IsDirty && dialogs?.DiscardPresetEdits() != true)) { Raise(); return; }
            LoadDraft(value);
        }
    }
    public RelayCommand NewCommand { get; }
    public RelayCommand AddSoftwareCommand { get; }
    public RelayCommand AddFontCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand StartCommand { get; }
    public void Add(PresetItem item)
    {
        if (IsBusy || Items.Any(row => row.Item.ToRequest().Id == item.ToRequest().Id)) return;
        Items.Add(new(item, row => { if (!IsBusy) Items.Remove(row); }));
    }
    private void MarkDirty() { dirty = true; Raise(nameof(IsDirty)); Raise(nameof(EditNotice)); }
    private void LoadDraft(InstallationPreset? preset)
    {
        loading = true;
        try
        {
            selected = preset; id = preset?.Id ?? Guid.NewGuid().ToString("N"); PresetName = preset?.Name ?? "New preset";
            Items.Clear(); foreach (var item in preset?.Items ?? []) Items.Add(new(item, row => { if (!IsBusy) Items.Remove(row); }));
            dirty = false; Raise(nameof(SelectedPreset)); Raise(nameof(IsDirty)); Raise(nameof(EditNotice));
        }
        finally { loading = false; }
    }
    public async Task ReloadAsync(string? selectId = null)
    {
        if (service is null) return;
        try
        {
            var inventory = await service.ListAsync(); IsLoaded = true; Diagnostics = inventory.Warnings; var previousId = selectId ?? selected?.Id;
            loading = true;
            try
            {
                Presets.Clear(); foreach (var preset in inventory.Presets) Presets.Add(preset);
                selected = Presets.FirstOrDefault(preset => preset.Id == previousId) ?? (IsDirty ? null : Presets.FirstOrDefault());
                Raise(nameof(SelectedPreset));
            }
            finally { loading = false; }
            if (selectId is not null || !IsDirty) LoadDraft(selected);
            Status = inventory.Warnings.Count > 0 ? string.Join("\n", inventory.Warnings) : $"{inventory.Presets.Count} saved presets available.";
        }
        catch (Exception ex) { IsLoaded = false; Diagnostics = ["Cannot read presets: " + ex.Message]; Status = Diagnostics[0]; }
    }
    public Task SaveAsync() => Work(async token =>
    {
        var preset = new InstallationPreset { SchemaVersion = 1, Id = id, Name = PresetName, Items = Items.Select(row => row.Item).ToArray() };
        await Require(service).SaveAsync(preset, token);
        await ReloadAsync(preset.Id); await shell.RefreshAsync(); Status = "Preset saved as portable JSON.";
    });
    public Task StartAsync() => Work(async token =>
    {
        if (IsDirty || selected is null) throw new InvalidOperationException("Save or select a saved preset before starting it.");
        if (shell.InstallationQueue.IsBusy) throw new InvalidOperationException("An installation queue is already active.");
        var resolved = await Require(service).ResolveAsync(selected.Id, token);
        if (!resolved.CanStart) { Status = string.Join("\n", resolved.Problems); return; }
        if (resolved.Items.Any(item => item.Request.Kind == InstallationKind.Font) && !FontLicenseConfirmed) throw new InvalidOperationException("Confirm font licenses before starting a preset containing fonts.");
        if (shell.InstallationQueue.Count > 0 && dialogs?.ReplacePendingQueue() != true) { Status = "Existing queue retained."; return; }
        shell.InstallationQueue.LoadPreset(resolved);
        await shell.InstallationQueue.StartAsync(); Status = shell.InstallationQueue.Status;
    });
}

public sealed class HistoryViewModel : ImportWorkspace
{
    private readonly IHistoryService? service;
    private readonly IPresetsHistoryDialogs? dialogs;
    private readonly List<HistorySession> all = [];
    private IReadOnlyList<string> warnings = [];
    private string search = "";
    private HistorySession? selected;
    public HistoryViewModel(IHistoryService? service, IPresetsHistoryDialogs? dialogs) : base("Past installs", "See what was installed and save a report when needed.")
    {
        this.service = service; this.dialogs = dialogs;
        RefreshCommand = new(() => Work(_ => ReloadAsync()), ex => Status = ex.Message);
        ExportJsonCommand = new(() => ExportAsync(HistoryExportFormat.Json), ex => Status = ex.Message);
        ExportCsvCommand = new(() => ExportAsync(HistoryExportFormat.Csv), ex => Status = ex.Message);
    }
    public bool IsLoaded { get; private set; }
    public IReadOnlyList<string> Diagnostics => warnings;
    public ObservableCollection<HistorySession> Sessions { get; } = [];
    public int RecentCount => all.Count(session => session.StartedUtc >= DateTimeOffset.UtcNow.AddDays(-30) && session.StartedUtc <= DateTimeOffset.UtcNow);
    public string Search { get => search; set { search = value ?? ""; Raise(); Filter(); } }
    public HistorySession? SelectedSession { get => selected; set { selected = value; Raise(); Raise(nameof(Details)); } }
    public string Details
    {
        get
        {
            if (SelectedSession is not { } session) return "Select a session to inspect its recorded results.";
            var text = new StringBuilder($"Session: {session.Id}\nState: {session.State}\nStarted: {session.StartedUtc:O}\nFinished: {session.FinishedUtc?.ToString("O") ?? "No completion event"}\nRequested: {session.RequestedCount?.ToString() ?? "Unknown"}\nRestart reported: {session.RestartRequired}\nSources: {string.Join(", ", session.Sources)}\n");
            foreach (var item in session.Items) text.Append($"\n{item.Name ?? item.Id}\nID: {item.Id}\nKind: {item.Kind?.ToString() ?? "Not recorded"}\nOutcome: {item.Outcome?.ToString() ?? "Unknown (no result)"}\nExit code: {item.ExitCode?.ToString() ?? "Not recorded"}\nRestart: {item.RestartRequired}\nStarted: {item.StartedUtc:O}\nFinished: {item.FinishedUtc?.ToString("O") ?? "Unknown"}\n{item.Message}\n");
            foreach (var problem in session.Problems) text.Append("\nWarning: " + problem);
            return text.ToString();
        }
    }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public async Task ReloadAsync()
    {
        if (service is null) return;
        try
        {
            var inventory = await service.ReadAsync(); IsLoaded = true; warnings = inventory.Warnings; all.Clear(); all.AddRange(inventory.Sessions);
            Filter(); Raise(nameof(RecentCount)); Status = warnings.Count == 0 ? $"{all.Count} recorded sessions. Incomplete can mean active or interrupted; finished does not mean every item succeeded." : string.Join("\n", warnings);
        }
        catch (Exception ex) { IsLoaded = false; warnings = ["Cannot read history: " + ex.Message]; all.Clear(); Filter(); Raise(nameof(RecentCount)); Status = "Cannot read history: " + ex.Message; }
    }
    private void Filter()
    {
        var previous = selected?.Id; var query = Search.Trim(); Sessions.Clear();
        foreach (var session in all.Where(session => query.Length == 0 || (session.Id + " " + session.State + " " + string.Join(" ", session.Items.Select(item => item.Id + " " + item.Name + " " + item.Outcome + " " + item.ExitCode + " " + item.Message))).Contains(query, StringComparison.OrdinalIgnoreCase))) Sessions.Add(session);
        SelectedSession = Sessions.FirstOrDefault(session => session.Id == previous) ?? Sessions.FirstOrDefault();
    }
    public Task ExportAsync(HistoryExportFormat format) => Work(async token =>
    {
        var snapshot = SelectedSession ?? throw new InvalidOperationException("Select a session to export.");
        var reportWarnings = warnings.ToArray();
        var destination = dialogs?.SaveReport(format); if (destination is null) { Status = "Export cancelled."; return; }
        await Require(service).ExportAsync(snapshot, format, destination, reportWarnings, overwrite: true, token: token);
        Status = "Report exported to " + destination;
    });
}
