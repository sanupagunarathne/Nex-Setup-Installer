using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Presets;

namespace NexInstaller.App.ViewModels;

public sealed class CollectionInstallerViewModel(string sourcePath, string? entryPoint = null) : ObservableObject
{
    public string SourcePath { get; } = sourcePath;
    public string? EntryPoint { get; } = entryPoint;
    private string name = Path.GetFileNameWithoutExtension(sourcePath.TrimEnd('/', '\\'));
    private string version = ReadVersion(entryPoint is null ? sourcePath : Path.Combine(sourcePath, entryPoint));
    public string Name { get => name; set { name = value; Raise(); } }
    public string Version { get => version; set { version = value; Raise(); } }
    public RelayCommand? RemoveCommand { get; set; }
    private static string ReadVersion(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).ProductVersion?.Trim() is { Length: > 0 } value ? value : "Unspecified"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return "Unspecified"; }
    }
}

public sealed partial class PackageManagerViewModel
{
    private readonly IPresetService? presets;
    private string collectionName = "";
    private string collectionId = Guid.NewGuid().ToString("N");
    public string CollectionName { get => collectionName; set { collectionName = value ?? ""; Raise(); } }
    public bool SaveCollectionAsPreset { get; set; } = true;
    public ObservableCollection<CollectionInstallerViewModel> CollectionInstallers { get; } = [];
    public RelayCommand BrowseCollectionCommand => new(() =>
    {
        if (!IsIdle) return;
        foreach (var path in Require(dialogs).Installers()) AddCollectionSource(path);
    });
    public RelayCommand BrowseCollectionFolderCommand => new(() =>
    {
        if (!IsIdle) return;
        var path = Require(dialogs).Folder(); if (path is null) return;
        var entry = Require(dialogs).EntryPoint(path); if (entry is null) return;
        AddCollectionSource(path, Path.GetRelativePath(path, entry));
    });
    public RelayCommand ClearCollectionCommand => new(() =>
    {
        if (!IsIdle) return;
        CollectionInstallers.Clear(); CollectionName = ""; collectionId = Guid.NewGuid().ToString("N");
    });
    public AsyncCommand SaveCollectionCommand => new(SaveCollectionAsync, ex => Status = ex.Message);
    public void AddCollectionSource(string path, string? entryPoint = null)
    {
        if (!IsIdle || CollectionInstallers.Any(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase) && string.Equals(item.EntryPoint, entryPoint, StringComparison.OrdinalIgnoreCase))) return;
        var row = new CollectionInstallerViewModel(path, entryPoint);
        row.RemoveCommand = new(() => { if (IsIdle) CollectionInstallers.Remove(row); });
        CollectionInstallers.Add(row);
    }
    public Task SaveCollectionAsync() => Work(async token =>
    {
        var name = LibraryFolder.Normalize(CollectionName);
        if (name.Length == 0) throw new InvalidOperationException("Enter a collection name, such as Adobe products.");
        var sources = CollectionInstallers.Select(item => new PackageImportRequest(item.SourcePath, item.EntryPoint,
            item.Name.Trim(), item.Version.Trim(), Architecture, Category, RequiresAdministrator: RequiresAdministrator, Folder: name)).ToArray();
        if (sources.Length == 0 || sources.Length > 1000) throw new InvalidOperationException("Choose between 1 and 1000 installers for this collection.");
        if (sources.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 160 || item.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(item.Version) || item.Version.Length > 80 || item.Version.Any(char.IsControl)))
            throw new InvalidOperationException("Each installer needs an app name (up to 160 characters) and version (up to 80 characters), without control characters.");
        var savePreset = SaveCollectionAsPreset;
        if (savePreset) Require(presets);
        if (!Require(dialogs).Confirm($"Save {sources.Length} installers in '{name}'{(savePreset ? " and create a reusable saved setup" : "")}? Nothing will be installed."))
        { Status = "Collection cancelled before copying."; return; }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var imported = 0; var reused = 0;
        try
        {
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var result = await Require(importer).ImportAsync(source, Progress, token);
                    if (result.Manifest is null) throw new InvalidDataException(result.Message);
                    ids.Add(result.Manifest.Id);
                    if (result.Imported) imported++; else reused++;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
                { failures.Add(source.Name + ": " + ex.Message); }
            }
            if (failures.Count > 0)
            {
                Status = $"{imported} imported, {reused} reused, {failures.Count} failed. Completed imports were kept. Fix the sources and retry; no preset was saved.\n" + string.Join("\n", failures);
                return;
            }
            if (savePreset)
                await Require(presets).SaveAsync(new InstallationPreset { SchemaVersion = 1, Id = collectionId, Name = name,
                    Items = ids.Select(id => new PresetItem { Kind = InstallationKind.Software, PackageId = id }).ToArray() }, token);
        }
        finally { await shell.RefreshAsync(); }
        Status = $"Collection '{name}' saved: {imported} imported, {reused} reused." +
            (savePreset ? " Open Saved setups to install this collection on another laptop." : " Open Apps and filter by folder to select these installers.") +
            (reused > 0 ? " Existing installers retain their original folders and are referenced by the preset." : "") + " Nothing was installed.";
        shell.RecordOperation("collection.import", $"Saved collection with {ids.Count} app references.");
    });
}
