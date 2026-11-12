using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NexInstaller.Core.Packages;

namespace NexInstaller.Infrastructure.Packages;

public sealed class JsonPackageManifestReader : IPackageManifestReader
{
    public const int MaximumBytes = 1_048_576;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter<PackageArchitecture>(allowIntegerValues: false),
            new JsonStringEnumConverter<InstallerType>(allowIntegerValues: false),
            new JsonStringEnumConverter<InstallationMode>(allowIntegerValues: false) }
    };

    public ManifestReadResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            return new(null, ["Manifest is empty or exceeds the 1 MiB limit."]);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicateProperties(document.RootElement);
            var manifest = JsonSerializer.Deserialize<PackageManifest>(json, Options);
            if (manifest is null) return new(null, ["Manifest must be a JSON object."]);
            var errors = Validate(manifest);
            return new(errors.Count == 0 ? manifest : null, errors);
        }
        catch (JsonException ex)
        {
            return new(null, [$"Invalid manifest JSON at {ex.Path ?? "root"}: {ex.Message}"]);
        }
    }

    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate property: {property.Name}.");
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicateProperties(child);
    }

    private static List<string> Validate(PackageManifest m)
    {
        var errors = new List<string>();
        if (m.SchemaVersion != 1) errors.Add("schemaVersion must be 1.");
        if (m.Id is null || !Regex.IsMatch(m.Id, "^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)) errors.Add("id must be a lowercase identifier of 1–80 characters.");
        CheckText(m.Name, 160, "name", errors);
        CheckText(m.Version, 80, "version", errors);
        CheckText(m.Category, 80, "category", errors);
        if (m.Folder is not null)
        {
            try
            {
                if (NexInstaller.Core.Importing.LibraryFolder.Normalize(m.Folder) != m.Folder || m.Folder.Length == 0)
                    errors.Add("folder must be a nonempty folder name without surrounding whitespace.");
            }
            catch (ArgumentException ex) { errors.Add(ex.Message); }
        }
        if (m.Description?.Length > 4000) errors.Add("description is longer than 4000 characters.");
        if (m.InstallationMode == InstallationMode.VerifiedUnattended && string.IsNullOrWhiteSpace(m.VerifiedProfileId))
            errors.Add("VerifiedUnattended requires a verifiedProfileId. A declaration does not verify a profile.");
        string? entry = Normalize(m.EntryPoint, errors);
        var extension = m.InstallerType == InstallerType.Msi ? ".msi" : ".exe";
        if (entry is not null && !entry.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) errors.Add("entryPoint extension does not match installerType.");
        if (m.Files is null || m.Files.Length == 0 || m.Files.Length > 10000) errors.Add("files must contain 1–10000 file records.");
        else
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in m.Files)
            {
                if (file is null) { errors.Add("files cannot contain null records."); continue; }
                var path = Normalize(file.Path, errors);
                if (path is not null && !paths.Add(path)) errors.Add($"Duplicate file path: {path}.");
                if (file.Sha256 is null || !Regex.IsMatch(file.Sha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) errors.Add("Each file needs a 64-character hexadecimal sha256.");
            }
            if (entry is not null && !paths.Contains(entry)) errors.Add("entryPoint must be listed in files.");
        }
        return errors;
    }

    private static void CheckText(string? value, int max, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value != value.Trim() || value.Any(char.IsControl))
            errors.Add($"{name} must contain 1–{max} characters, without surrounding whitespace or control characters.");
    }

    private static string? Normalize(string? path, List<string> errors)
    {
        try { return PortablePackagePath.Normalize(path!); }
        catch (ArgumentException ex) { errors.Add(ex.Message); return null; }
    }
}
