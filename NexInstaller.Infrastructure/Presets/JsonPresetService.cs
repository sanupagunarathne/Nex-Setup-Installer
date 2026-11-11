using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Packages;
using NexInstaller.Core.Presets;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Infrastructure.Presets;

public sealed class JsonPresetService(LibraryPaths paths, IPackageCatalogService catalog) : IPresetService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false, MaxDepth = 16, WriteIndented = true,
        Converters = { new JsonStringEnumConverter<InstallationKind>(allowIntegerValues: false) },
        IgnoreReadOnlyProperties = true
    };
    public static void Validate(InstallationPreset preset)
    {
        if (preset.SchemaVersion != 1 || !Guid.TryParseExact(preset.Id, "N", out _) || preset.Id != preset.Id.ToLowerInvariant()) throw new InvalidDataException("Preset needs schemaVersion 1 and a lowercase GUID ID without dashes.");
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 160 || preset.Name != preset.Name.Trim() || preset.Name.Any(char.IsControl)) throw new InvalidDataException("Preset name needs 1–160 characters without control characters or surrounding whitespace.");
        if (preset.Items is null || preset.Items.Length > 1000) throw new InvalidDataException("Preset must contain an array of at most 1000 items.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in preset.Items)
        {
            if (item is null) throw new InvalidDataException("Preset items cannot be null.");
            if (item.Kind == InstallationKind.Software)
            {
                if (item.PackageId is null || !Regex.IsMatch(item.PackageId, "^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant) || item.FontRelativePath is not null || item.Sha256 is not null) throw new InvalidDataException("Software items need a package ID only.");
            }
            else if (item.Kind == InstallationKind.Font)
            {
                var relative = item.FontRelativePath?.Replace('\\', '/');
                if (item.PackageId is not null || relative is null || !relative.StartsWith("fonts/", StringComparison.Ordinal) || item.Sha256 is null || !Regex.IsMatch(item.Sha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Font items need a relative font path and SHA-256 only.");
                try { PortablePackagePath.Normalize("packages/font/" + relative[6..]); }
                catch (ArgumentException ex) { throw new InvalidDataException("Unsafe preset font path.", ex); }
                if (Path.GetExtension(relative).ToLowerInvariant() is not (".ttf" or ".otf")) throw new InvalidDataException("Unsupported font file in preset.");
            }
            else throw new InvalidDataException("Unsupported preset item kind.");
            if (!ids.Add(item.ToRequest().Id)) throw new InvalidDataException("Duplicate or ambiguous preset item IDs.");
        }
    }
    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _) || id != id.ToLowerInvariant()) throw new InvalidDataException("Invalid preset ID.");
        return Path.Combine(paths.Presets, id + ".json");
    }
    private static InstallationPreset Read(string path)
    {
        LibraryStorage.CheckPath(path);
        using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 2 * 1024 * 1024) throw new InvalidDataException("Preset exceeds 2 MiB.");
        var preset = JsonSerializer.Deserialize<InstallationPreset>(input, Json) ?? throw new InvalidDataException("Empty preset.");
        Validate(preset);
        if (Path.GetFileName(path) != preset.Id + ".json") throw new InvalidDataException("Preset filename must match its ID.");
        return preset;
    }
    public Task<PresetInventory> ListAsync(CancellationToken token = default) => Task.Run(() =>
    {
        var presets = new List<InstallationPreset>(); var warnings = new List<string>();
        try
        {
            LibraryStorage.CheckPath(paths.Presets);
            if (!Directory.Exists(paths.Presets)) return new PresetInventory(presets, warnings);
            foreach (var file in Directory.EnumerateFiles(paths.Presets).Where(file => Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)))
            {
                token.ThrowIfCancellationRequested();
                try { presets.Add(Read(file)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException) { warnings.Add(Path.GetFileName(file) + ": " + ex.Message); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("Cannot fully read presets: " + ex.Message); }
        return new PresetInventory(presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
    }, token);
    public Task SaveAsync(InstallationPreset preset, CancellationToken token = default) => Task.Run(() =>
    {
        Validate(preset);
        var json = JsonSerializer.Serialize(preset, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) throw new InvalidDataException("Preset exceeds 2 MiB.");
        token.ThrowIfCancellationRequested();
        var storage = new LibraryStorage(paths); using var gate = storage.BeginWrite(); storage.CreateDirectory(paths.Presets);
        var target = PathFor(preset.Id); LibraryStorage.CheckPath(target);
        // Never replace a malformed file under a valid-looking filename.
        if (File.Exists(target)) Read(target);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew))) { writer.Write(json); writer.Flush(); ((FileStream)writer.BaseStream).Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);
    public Task<ResolvedPreset> ResolveAsync(string id, CancellationToken token = default) => Task.Run(async () =>
    {
        var preset = Read(PathFor(id)); var software = await catalog.DiscoverAsync(token);
        var resolved = new List<ResolvedPresetItem>(); var problems = new List<string>();
        foreach (var item in preset.Items)
        {
            token.ThrowIfCancellationRequested();
            if (item.Kind == InstallationKind.Software)
            {
                var matches = software.Packages.Where(package => package.Manifest.Id == item.PackageId).ToArray();
                if (matches.Length != 1 || !matches[0].IsAvailable) { problems.Add("Missing, duplicate, or unavailable software: " + item.PackageId); continue; }
                resolved.Add(new(item.ToRequest(), matches[0].Manifest.Name));
            }
            else
            {
                try
                {
                    var full = Path.Combine(paths.Root, item.FontRelativePath!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                    if (!LibraryStorage.Hash(full, token).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Font SHA-256 changed.");
                    resolved.Add(new(item.ToRequest(), Path.GetFileName(full)));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { problems.Add(item.FontRelativePath + ": " + ex.Message); }
            }
        }
        if (preset.Items.Length == 0) problems.Add("The preset is empty.");
        return new ResolvedPreset(preset, resolved, problems);
    }, token);
}
