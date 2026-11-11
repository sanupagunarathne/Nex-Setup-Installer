using System.Text.Json;
using NexInstaller.Core.Importing;
namespace NexInstaller.Infrastructure.Importing;
public sealed class CategoryService(LibraryStorage storage) : ICategoryService
{
    private string FilePath => Path.Combine(storage.Paths.Root, "categories.json");
    private static void Validate(string category)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Length > 80 || category != category.Trim() || category.Any(char.IsControl))
            throw new ArgumentException("Category needs 1–80 characters without surrounding whitespace or control characters.");
    }
    private IReadOnlyList<string> Saved()
    {
        LibraryStorage.CheckPath(FilePath);
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > 128 * 1024) throw new InvalidDataException("Category configuration is too large.");
        var categories = JsonSerializer.Deserialize<string[]>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Invalid category configuration.");
        if (categories.Length > 1000) throw new InvalidDataException("Too many categories.");
        foreach (var category in categories) Validate(category);
        return categories;
    }
    public IReadOnlyList<string> List() => Saved().Concat(storage.ReadManifests().Select(item => item.Manifest.Category))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public void Add(string category)
    {
        Validate(category);
        using var gate = storage.BeginWrite();
        if (List().Contains(category, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Category already exists.");
        var saved = Saved();
        if (saved.Count >= 1000) throw new InvalidOperationException("Category limit reached.");
        WriteAtomic(FilePath, JsonSerializer.Serialize(saved.Append(category), LibraryStorage.Json));
    }
    public void Remove(string category)
    {
        Validate(category);
        using var gate = storage.BeginWrite();
        if (storage.ReadManifests().Any(item => item.Manifest.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Category is in use. Assign its packages to another category first.");
        WriteAtomic(FilePath, JsonSerializer.Serialize(Saved().Where(name => !name.Equals(category, StringComparison.OrdinalIgnoreCase)), LibraryStorage.Json));
    }
    public void Assign(string packageId, string category)
    {
        Validate(category);
        using var gate = storage.BeginWrite();
        var canonical = List().FirstOrDefault(name => name.Equals(category, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Create the category before assigning it.");
        var matches = storage.ReadManifests().Where(item => item.Manifest.Id == packageId).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Package ID is missing or ambiguous.");
        WriteAtomic(matches[0].Path, JsonSerializer.Serialize(matches[0].Manifest with { Category = canonical }, LibraryStorage.Json));
    }
    private static void WriteAtomic(string target, string text)
    {
        LibraryStorage.CheckPath(target);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew))) { writer.Write(text); writer.Flush(); ((FileStream)writer.BaseStream).Flush(true); }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
