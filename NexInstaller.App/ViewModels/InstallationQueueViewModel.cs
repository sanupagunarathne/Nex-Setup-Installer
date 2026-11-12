using System.Collections.ObjectModel;
using System.IO;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Presets;
using NexInstaller.Core.Installation;
namespace NexInstaller.App.ViewModels;

public sealed class InstallationQueueItem(InstallationRequest request, string name, Action<InstallationQueueItem> remove) : ObservableObject
{
    public InstallationRequest Request { get; } = request;
    public string Name { get; } = name;
    private string status = "Queued";
    public string Status { get => status; set { status = value; Raise(); } }
    public RelayCommand RemoveCommand => new(() => remove(this));
}
public sealed class InstallationQueueViewModel : ObservableObject
{
    private readonly IInstallationEngine? engine;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private double percent;
    private string status = "Add software or licensed fonts to prepare a queue. Each item requires confirmation before execution.";
    public InstallationQueueViewModel(IInstallationEngine? engine)
    {
        this.engine = engine;
        Items.CollectionChanged += (_, _) => { Raise(nameof(HasItems)); Raise(nameof(HasContent)); Raise(nameof(IsEmpty)); };
        Results.CollectionChanged += (_, _) => { Raise(nameof(HasContent)); Raise(nameof(IsEmpty)); };
        StartCommand = new(StartAsync, ex => Status = ex.Message);
        ClearCommand = new(() => { if (!IsBusy) { Items.Clear(); Raise(nameof(Count)); } });
        StopCommand = new(() => { cancellation?.Cancel(); Status = "Stop requested. Waiting for any active installer to finish; it will not be killed."; });
    }
    public Func<Task>? AfterRunAsync { get; set; }
    public ObservableCollection<InstallationQueueItem> Items { get; } = [];
    public ObservableCollection<InstallationResult> Results { get; } = [];
    public int Count => Items.Count;
    public bool HasItems => Items.Count > 0;
    public bool HasContent => HasItems || Results.Count > 0;
    public bool IsEmpty => !HasContent;
    public bool HasStarted { get; private set; }
    public bool IsBusy { get => busy; private set { busy = value; Raise(); Raise(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public bool IsLibraryChanging { get; set; }
    public double Percent { get => percent; private set { percent = value; Raise(); } }
    public bool ContinueOnFailure { get; set; }
    public string Status { get => status; private set { status = value; Raise(); } }
    public AsyncCommand StartCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand StopCommand { get; }
    public void AddSoftware(IEnumerable<PackageRowViewModel> rows)
    {
        if (IsBusy) { Status = "Wait for the current queue before changing it."; return; }
        foreach (var row in rows.Where(row => row.IsAvailable)) Add(new(InstallationKind.Software, row.Package.Manifest.Id), row.Name);
    }
    public void AddFont(FontRecord font, string libraryRoot, bool licenseConfirmed)
    {
        if (!licenseConfirmed) { Status = "Confirm font licenses before queuing a font."; return; }
        if (IsBusy) { Status = "Wait for the current queue before changing it."; return; }
        Add(new(InstallationKind.Font, "font-" + font.Sha256.ToLowerInvariant(), Path.GetRelativePath(libraryRoot, font.Path), font.Sha256), font.Name);
    }
    private void Add(InstallationRequest request, string name)
    {
        if (Items.Any(item => item.Request.Id == request.Id)) return;
        Items.Add(new(request, name, item => { if (!IsBusy) { Items.Remove(item); Raise(nameof(Count)); } }));
        Raise(nameof(Count)); Status = "Queue updated. Start will verify files and ask for technician confirmation.";
    }
    public void LoadPreset(ResolvedPreset preset)
    {
        if (IsBusy) throw new InvalidOperationException("The queue is active.");
        if (!preset.CanStart || preset.Items.Count != preset.Preset.Items.Length || preset.Items.Select(item => item.Request.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != preset.Items.Count)
            throw new InvalidDataException("Every preset item must resolve unambiguously before replacing the queue.");
        Items.Clear(); foreach (var item in preset.Items) Add(item.Request, item.Name);
    }
    public async Task StartAsync()
    {
        if (IsLibraryChanging) { Status = "Wait for the library change to finish before starting installation."; return; }
        if (IsBusy) return;
        if (Items.Count == 0) { Status = "The installation queue is empty."; return; }
        if (engine is null) { Status = "Installation services are unavailable."; return; }
        using var source = new CancellationTokenSource(); cancellation = source;
        var snapshot = Items.ToArray(); var continueOnFailure = ContinueOnFailure;
        HasStarted = true; Raise(nameof(HasStarted));
        IsBusy = true; Percent = 0; Results.Clear();
        foreach (var item in snapshot) item.Status = "Waiting";
        var progressGate = new object(); var acceptProgress = true;
        void FinishProgress() { lock (progressGate) acceptProgress = false; }
        try
        {
            var progress = new Progress<InstallationProgress>(step =>
            {
                lock (progressGate)
                {
                    if (!acceptProgress) return;
                    Percent = step.Total == 0 ? 0 : 100d * step.Completed / step.Total; Status = step.Stage;
                }
            });
            var results = await engine.RunAsync(snapshot.Select(item => item.Request).ToArray(), continueOnFailure, progress, source.Token);
            FinishProgress();
            ApplyResults(snapshot, results);
            Status = $"{results.Count}/{snapshot.Length} processed. " + (results.Any(result => result.RestartRequired) ? "Restart required or reported by an installer. NEX will not restart Windows." : "No restart reported.") +
                " Clear or remove completed items before starting another queue. Re-running requires fresh preflight and confirmations.";
        }
        catch (InstallationSessionException ex) { FinishProgress(); ApplyResults(snapshot, ex.Results); Status = ex.Message; }
        catch (OperationCanceledException) { FinishProgress(); Status = "Queue cancelled before execution."; }
        catch (Exception ex) { FinishProgress(); Status = "Queue stopped: " + ex.Message + " Check installation logs before retrying; an item may already have executed."; }
        finally { FinishProgress(); cancellation = null; IsBusy = false; if (AfterRunAsync is not null) await AfterRunAsync(); }
    }
    private void ApplyResults(InstallationQueueItem[] snapshot, IReadOnlyList<InstallationResult> results)
    {
        foreach (var result in results)
        {
            Results.Add(result);
            snapshot.Single(item => item.Request.Id == result.Id).Status = $"{result.Outcome} · exit {result.ExitCode?.ToString() ?? "none"}";
        }
        foreach (var item in snapshot.Where(item => item.Status == "Waiting")) item.Status = "Not started";
        Percent = 100d * results.Count / snapshot.Length;
    }

}
