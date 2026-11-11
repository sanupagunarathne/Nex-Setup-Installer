using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexInstaller.Core.History;
using NexInstaller.Core.Installation;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.Infrastructure.History;

public sealed class JsonHistoryService(string applicationDirectory, string localDataDirectory) : IHistoryService
{
    private readonly string[] directories = [Path.Combine(applicationDirectory, "logs"), Path.Combine(localDataDirectory, "NexInstaller", "logs")];
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, AllowDuplicateProperties = false, MaxDepth = 16,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter<InstallationOutcome>(allowIntegerValues: false), new JsonStringEnumConverter<InstallationKind>(allowIntegerValues: false), new JsonStringEnumConverter<HistorySessionState>(allowIntegerValues: false) } };
    private sealed record Entry(string EventId, string SessionId, DateTimeOffset TimestampUtc, string EventName, JsonElement Details, string Source, string Raw);
    public Task<HistoryInventory> ReadAsync(CancellationToken token = default) => Task.Run(() =>
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase); var warnings = new List<string>(); var damagedSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Warn(string message) { if (warnings.Count < 200) warnings.Add(message); else if (warnings.Count == 200) warnings.Add("Additional warnings omitted; history may be incomplete."); }
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LibraryStorage.CheckPath(directory); if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory, "installations-*.jsonl").OrderDescending(StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var source = (directory == directories[0] ? "portable/" : "local/") + Path.GetFileName(file);
                    try
                    {
                        LibraryStorage.CheckPath(file);
                        using var input = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (input.Length > 64 * 1024 * 1024) throw new InvalidDataException("Log exceeds 64 MiB read limit.");
                        using var reader = new StreamReader(input, new UTF8Encoding(false, true)); var number = 0;
                        while (true)
                        {
                            if (entries.Count >= 100000) { Warn("History read limit of 100000 events reached; results may be incomplete."); break; }
                            var (line, oversized) = ReadLine(reader, token); if (line is null) break; number++;
                            if (oversized) { Warn($"{source}:{number}: oversized event skipped."); continue; }
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 }); var root = document.RootElement;
                                // Reject ambiguous fields, including duplicate properties nested in details.
                                CheckDuplicates(root);
                                var eventId = root.GetProperty("eventId").GetString(); var sessionId = root.GetProperty("sessionId").GetString();
                                if (!Guid.TryParseExact(eventId, "N", out _) || !Guid.TryParseExact(sessionId, "N", out _)) throw new InvalidDataException("Invalid event/session ID.");
                                var entry = new Entry(eventId!, sessionId!, root.GetProperty("timestampUtc").GetDateTimeOffset(), root.GetProperty("eventName").GetString()!, root.GetProperty("details").Clone(), source, line);
                                if (entry.Details.ValueKind != JsonValueKind.Object || entry.EventName is not ("session.start" or "item.start" or "item.result" or "session.complete")) throw new InvalidDataException("Unsupported installation event.");
                                if (entries.TryGetValue(entry.EventId, out var previous))
                                {
                                    if (previous.Raw != line) { Warn(source + ": conflicting duplicate event ID."); damagedSessions.Add(previous.SessionId); damagedSessions.Add(entry.SessionId); }
                                    continue;
                                }
                                entries.Add(entry.EventId, entry);
                            }
                            catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException)
                            { Warn($"{source}:{number}: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException) { Warn(source + ": " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn("Cannot fully read installation log location: " + ex.Message); }
        }
        var sessions = entries.Values.GroupBy(entry => entry.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => Build(group.Key, group.OrderBy(entry => entry.TimestampUtc).ToArray(), damagedSessions.Contains(group.Key)))
            .OrderByDescending(session => session.StartedUtc).ToArray();
        return new HistoryInventory(sessions, warnings);
    }, token);
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) CheckDuplicates(child);
    }
    private static (string? Line, bool Oversized) ReadLine(TextReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); var oversized = false; var count = 0; int character;
        while ((character = reader.Read()) != -1)
        {
            if ((count++ & 4095) == 0) token.ThrowIfCancellationRequested();
            if (character == '\n') return (text.ToString().TrimEnd('\r'), oversized);
            if (text.Length < 256 * 1024) text.Append((char)character); else oversized = true;
        }
        token.ThrowIfCancellationRequested();
        return count == 0 ? (null, false) : (text.ToString().TrimEnd('\r'), oversized);
    }
    private static HistorySession Build(string id, Entry[] entries, bool conflict)
    {
        var problems = new List<string>(); var starts = new Dictionary<string, (string? Name, InstallationKind? Kind, DateTimeOffset Time)>(); var results = new List<HistoryItem>();
        DateTimeOffset started = entries[0].TimestampUtc; DateTimeOffset? finished = null; int? requested = null; int? completed = null; var startCount = 0; var endCount = 0;
        if (conflict) problems.Add("Conflicting duplicate event IDs were found; review source logs.");
        foreach (var entry in entries)
        {
            try
            {
                var details = entry.Details;
                switch (entry.EventName)
                {
                    case "session.start":
                        startCount++; started = entry.TimestampUtc; requested = details.GetProperty("count").GetInt32();
                        if (requested is < 1 or > 1000) throw new InvalidDataException("Invalid requested item count."); break;
                    case "session.complete":
                        if (details.GetProperty("requested").GetInt32() != requested) throw new InvalidDataException("Requested counts disagree.");
                        endCount++; finished = entry.TimestampUtc; completed = details.GetProperty("completed").GetInt32();
                        if (completed is < 0 or > 1000) throw new InvalidDataException("Invalid completed item count."); break;
                    case "item.start":
                        var itemId = details.GetProperty("Id").GetString() ?? throw new InvalidDataException("Missing item ID.");
                        if (!starts.TryAdd(itemId, (details.GetProperty("Name").GetString(), details.GetProperty("Kind").Deserialize<InstallationKind>(Json), entry.TimestampUtc))) problems.Add("Repeated item.start for " + itemId);
                        break;
                    case "item.result":
                        foreach (var field in new[] { "Id", "Outcome", "ExitCode", "RestartRequired", "Message", "StartedUtc", "FinishedUtc" })
                            if (!details.EnumerateObject().Any(property => property.Name.Equals(field, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Missing result field: " + field);
                        var result = details.Deserialize<InstallationResult>(Json) ?? throw new InvalidDataException("Empty result.");
                        if (string.IsNullOrWhiteSpace(result.Id) || !Enum.IsDefined(result.Outcome) || result.FinishedUtc < result.StartedUtc || result.StartedUtc == default || result.Message is null) throw new InvalidDataException("Invalid item result.");
                        results.Add(new(result.Id, null, null, result.Outcome, result.ExitCode, result.RestartRequired, result.StartedUtc, result.FinishedUtc, result.Message)); break;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException) { problems.Add(entry.EventName + ": " + ex.Message); }
        }
        results = results.Select(result => starts.TryGetValue(result.Id, out var start) ? result with { Name = start.Name, Kind = start.Kind } : result).ToList();
        foreach (var (itemId, start) in starts.Where(pair => !results.Any(result => result.Id == pair.Key)))
            results.Add(new(itemId, start.Name, start.Kind, null, null, false, start.Time, null, "Execution was announced but no result was recorded. It may be active or interrupted; do not automatically retry."));
        if (startCount != 1) problems.Add("Expected one session.start event.");
        if (endCount > 1) problems.Add("Repeated session.complete events.");
        if (results.GroupBy(item => item.Id).Any(group => group.Count() > 1)) problems.Add("Repeated item results; inspect logs rather than choosing a winner.");
        if (finished < started) problems.Add("Session timestamps are out of order.");
        if (endCount == 1 && (completed != results.Count(item => item.Outcome is not null) || results.Any(item => item.Outcome is null) || requested < completed)) problems.Add("Session completion counts do not match recorded outcomes.");
        var state = problems.Count > 0 ? HistorySessionState.Inconsistent : endCount == 1 ? HistorySessionState.Finished : HistorySessionState.Incomplete;
        return new(id, started, finished, requested, state, results, problems, entries.Select(entry => entry.Source).Distinct().ToArray());
    }
    public Task ExportAsync(HistorySession session, HistoryExportFormat format, string destination, IReadOnlyList<string> warnings, bool overwrite = false, CancellationToken token = default) => Task.Run(() =>
    {
        var target = Path.GetFullPath(destination); LibraryStorage.CheckPath(target);
        var extension = format == HistoryExportFormat.Json ? ".json" : format == HistoryExportFormat.Csv ? ".csv" : throw new ArgumentOutOfRangeException(nameof(format));
        if (!Path.GetExtension(target).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Report extension must match the chosen format.");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested();
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew), new UTF8Encoding(format == HistoryExportFormat.Csv)))
            {
                if (format == HistoryExportFormat.Json) writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, exportedUtc = DateTimeOffset.UtcNow, warnings, session }, Json));
                else
                {
                    writer.WriteLine("SessionId,SessionState,SessionStartedUtc,SessionFinishedUtc,RequestedCount,ItemId,Name,Kind,Outcome,ExitCode,RestartRequired,ItemStartedUtc,ItemFinishedUtc,Message,Warnings");
                    foreach (var item in session.Items.Cast<HistoryItem?>().DefaultIfEmpty(null))
                    {
                        token.ThrowIfCancellationRequested();
                        var fields = new[] { session.Id, session.State.ToString(), session.StartedUtc.ToString("O"), session.FinishedUtc?.ToString("O"), session.RequestedCount?.ToString(CultureInfo.InvariantCulture), item?.Id, item?.Name, item?.Kind?.ToString(), item?.Outcome?.ToString() ?? "No result recorded", item?.ExitCode?.ToString(CultureInfo.InvariantCulture), item?.RestartRequired.ToString(), item?.StartedUtc.ToString("O"), item?.FinishedUtc?.ToString("O"), item?.Message, string.Join("; ", warnings.Concat(session.Problems)) };
                        writer.WriteLine(string.Join(",", fields.Select(Csv)));
                    }
                }
                writer.Flush(); ((FileStream)writer.BaseStream).Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);
    private static string Csv(string? value)
    {
        value ??= ""; var trimmed = value.TrimStart();
        if (value.Length > 0 && (value[0] is '\t' or '\r' or '\n' || (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
