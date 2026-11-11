using System.Buffers.Binary;
using NexInstaller.Core.Importing;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Infrastructure.Importing;

public sealed class FontImportService(LibraryStorage storage) : IFontImportService
{
    private static bool Supported(string path) => Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase);
    public Task<IReadOnlyList<FontRecord>> ListAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<FontRecord>>(() =>
    {
        LibraryStorage.CheckPath(storage.Paths.Fonts);
        if (!Directory.Exists(storage.Paths.Fonts)) return [];
        return LibraryStorage.Files(storage.Paths.Fonts).Where(Supported).Select(path =>
            new FontRecord(Path.GetFileName(path), path, LibraryStorage.Hash(path, token))).ToArray();
    }, token);

    public Task<IReadOnlyList<FontImportResult>> ImportAsync(IReadOnlyList<string> sources, bool licenseConfirmed, IProgress<string>? progress = null, CancellationToken token = default) => Task.Run<IReadOnlyList<FontImportResult>>(() =>
    {
        if (!licenseConfirmed) throw new InvalidOperationException("Confirm the font licenses permit use on the destination devices.");
        using var gate = storage.BeginWrite();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(storage.Paths.Fonts))
            foreach (var existing in LibraryStorage.Files(storage.Paths.Fonts).Where(Supported)) hashes.Add(LibraryStorage.Hash(existing, token));
        var results = new List<FontImportResult>();
        var files = new List<string>();
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                LibraryStorage.CheckPath(source);
                if (Directory.Exists(source))
                {
                    if (LibraryStorage.IsWithin(storage.Paths.Root, source) || LibraryStorage.IsWithin(source, storage.Paths.Root)) throw new IOException("Font import folders must be outside the library.");
                    var supported = LibraryStorage.Files(source).Where(Supported).ToArray();
                    if (supported.Length == 0) results.Add(new(source, "Skipped", "No TTF/OTF files found."));
                    files.AddRange(supported);
                }
                else files.Add(source);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { results.Add(new(source, "Failed", ex.Message)); }
        }
        foreach (var source in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            string? temporary = null;
            try
            {
                if (!Supported(source)) throw new InvalidDataException("Only TTF and OTF font files are supported.");
                if (LibraryStorage.IsWithin(source, storage.Paths.Root)) throw new IOException("Choose source files outside the library.");
                PortablePackagePath.Normalize("packages/font/" + Path.GetFileName(source));
                progress?.Report("Validating and hashing " + Path.GetFileName(source));
                ValidateFont(source);
                var hash = LibraryStorage.Hash(source, token);
                if (hashes.Contains(hash)) { results.Add(new(source, "Duplicate", "Identical font content already exists.")); continue; }
                var targetDirectory = Path.Combine(storage.Paths.Fonts, hash.ToLowerInvariant());
                storage.CreateDirectory(targetDirectory);
                temporary = Path.Combine(targetDirectory, Guid.NewGuid().ToString("N") + ".tmp");
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(temporary, FileMode.CreateNew))
                {
                    var buffer = new byte[1024 * 1024]; int read;
                    while ((read = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                    output.Flush(true);
                }
                if (LibraryStorage.Hash(temporary, token) != hash) throw new IOException("Source changed during import.");
                ValidateFont(temporary);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, Path.Combine(targetDirectory, Path.GetFileName(source)));
                hashes.Add(hash);
                results.Add(new(source, "Imported", "SHA-256 verified. Font has not been installed in Windows."));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { results.Add(new(source, "Failed", ex.Message)); }
            finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
        }
        return results;
    }, token);

    private static void ValidateFont(string path)
    {
        LibraryStorage.CheckPath(path);
        using var input = File.OpenRead(path);
        using var reader = new BinaryReader(input);
        if (input.Length < 12) throw new InvalidDataException("Font header is truncated.");
        var header = reader.ReadBytes(12);
        var signature = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (signature is not (0x00010000 or 0x4F54544F or 0x74727565)) throw new InvalidDataException("Invalid TTF/OTF signature.");
        var tables = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        if (tables == 0 || tables > 4096 || input.Length < 12 + tables * 16) throw new InvalidDataException("Invalid font table directory.");
        for (var index = 0; index < tables; index++)
        {
            var table = reader.ReadBytes(16);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(8));
            var length = BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(12));
            if ((ulong)offset + length > (ulong)input.Length) throw new InvalidDataException("Font table extends beyond the file.");
        }
    }
}
