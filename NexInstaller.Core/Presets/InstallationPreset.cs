using NexInstaller.Core.Installation;
namespace NexInstaller.Core.Presets;

public sealed record PresetItem
{
    public required InstallationKind Kind { get; init; }
    public string? PackageId { get; init; }
    public string? FontRelativePath { get; init; }
    public string? Sha256 { get; init; }
    public InstallationRequest ToRequest() => Kind == InstallationKind.Software ? new(Kind, PackageId!) : new(Kind, "font-" + Sha256!.ToLowerInvariant(), FontRelativePath, Sha256);
    public string Label => Kind == InstallationKind.Software ? "Software · " + PackageId : "Font · " + FontRelativePath;
}
public sealed record InstallationPreset
{
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required PresetItem[] Items { get; init; }
}
public sealed record PresetInventory(IReadOnlyList<InstallationPreset> Presets, IReadOnlyList<string> Warnings);
public sealed record ResolvedPresetItem(InstallationRequest Request, string Name);
public sealed record ResolvedPreset(InstallationPreset Preset, IReadOnlyList<ResolvedPresetItem> Items, IReadOnlyList<string> Problems)
{
    public bool CanStart => Problems.Count == 0 && Items.Count > 0;
}
public interface IPresetService
{
    Task<PresetInventory> ListAsync(CancellationToken token = default);
    Task SaveAsync(InstallationPreset preset, CancellationToken token = default);
    Task<ResolvedPreset> ResolveAsync(string id, CancellationToken token = default);
}
