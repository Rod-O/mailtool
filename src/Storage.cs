using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailTool;

/// <summary>
/// On-disk layout for the mailtool cache: messages, threads, events, sync
/// state, and the lookup index. Paths are rooted at <see cref="CacheRoot"/>,
/// which can be overridden via the <c>MAILTOOL_CACHE</c> environment variable.
/// </summary>
public static class Storage
{
    /// <summary>Root directory for all cache data. Override via <c>MAILTOOL_CACHE</c>.</summary>
    public static readonly string CacheRoot =
        Environment.GetEnvironmentVariable("MAILTOOL_CACHE")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mailtool", "cache");
    /// <summary>Subdirectory holding individual message JSON files (sharded by year/month).</summary>
    public static readonly string MessagesDir = Path.Combine(CacheRoot, "messages");
    /// <summary>Subdirectory holding thread reconstruction blobs.</summary>
    public static readonly string ThreadsDir = Path.Combine(CacheRoot, "threads");
    /// <summary>Subdirectory holding cached calendar events (sharded by start year/month).</summary>
    public static readonly string EventsDir = Path.Combine(CacheRoot, "events");
    /// <summary>Path to the per-folder sync state file (delta tokens, last-sync, message counts).</summary>
    public static readonly string StatePath = Path.Combine(CacheRoot, "state.json");
    /// <summary>Path to the global message index file (id → relative path; conversation → message ids).</summary>
    public static readonly string IndexPath = Path.Combine(CacheRoot, "index.json");
    /// <summary>Path to the calendar events index file.</summary>
    public static readonly string EventsIndexPath = Path.Combine(CacheRoot, "events_index.json");

    /// <summary>JSON serializer settings used for all persisted cache files.</summary>
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Creates the cache root, messages, and threads directories if missing.</summary>
    public static void EnsureDirs()
    {
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(MessagesDir);
        Directory.CreateDirectory(ThreadsDir);
    }

    /// <summary>
    /// Converts a Graph message/event/thread id into a safe filename component.
    /// Defends against path traversal: rejects empty/null input, '..' sequences,
    /// null bytes, and converts both '/' and '\' to '_' so an id can never
    /// escape the cache directory regardless of OS path semantics.
    /// </summary>
    public static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Cannot sanitize empty id.", nameof(id));

        var clean = id
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace('+', '-')
            .TrimEnd('=');

        if (clean.Length == 0 || clean.Contains("..") || clean.Contains('\0'))
            throw new ArgumentException($"Unsafe id rejected: '{id}'", nameof(id));

        return clean;
    }

    /// <summary>Returns the on-disk path for a message, sharded by received-year/month.</summary>
    public static string MessagePath(DateTimeOffset received, string id)
    {
        var folder = Path.Combine(MessagesDir, received.Year.ToString("D4"), received.Month.ToString("D2"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SanitizeId(id) + ".json");
    }

    /// <summary>Returns the on-disk path for a thread/conversation reconstruction file.</summary>
    public static string ThreadPath(string conversationId) =>
        Path.Combine(ThreadsDir, SanitizeId(conversationId) + ".json");

    /// <summary>Loads the sync state. Returns a fresh empty <see cref="State"/> if the file is missing or corrupt.</summary>
    public static State LoadState()
    {
        if (!File.Exists(StatePath)) return new State();
        try
        {
            return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
        }
        catch
        {
            return new State();
        }
    }

    /// <summary>Persists the sync state to disk.</summary>
    public static void SaveState(State state) =>
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOpts));

    /// <summary>Loads the message index. Returns a fresh empty <see cref="Index"/> if the file is missing or corrupt.</summary>
    public static Index LoadIndex()
    {
        if (!File.Exists(IndexPath)) return new Index();
        try
        {
            return JsonSerializer.Deserialize<Index>(File.ReadAllText(IndexPath)) ?? new Index();
        }
        catch
        {
            return new Index();
        }
    }

    /// <summary>Persists the message index to disk.</summary>
    public static void SaveIndex(Index index) =>
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOpts));

    /// <summary>Loads the cached message at <paramref name="relativePath"/> (relative to <see cref="CacheRoot"/>). Returns null if missing.</summary>
    public static JsonObject? LoadMessage(string relativePath)
    {
        var full = Path.Combine(CacheRoot, relativePath);
        if (!File.Exists(full)) return null;
        return JsonNode.Parse(File.ReadAllText(full))?.AsObject();
    }

    /// <summary>Enumerates every cached message file path on disk (recursive).</summary>
    public static IEnumerable<string> EnumerateMessageFiles() =>
        Directory.Exists(MessagesDir)
            ? Directory.EnumerateFiles(MessagesDir, "*.json", SearchOption.AllDirectories)
            : [];

    /// <summary>Returns the on-disk path for an event, sharded by start-year/month.</summary>
    public static string EventPath(DateTimeOffset start, string id)
    {
        var folder = Path.Combine(EventsDir, start.Year.ToString("D4"), start.Month.ToString("D2"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SanitizeId(id) + ".json");
    }

    /// <summary>Loads the calendar events index. Returns a fresh empty <see cref="EventsIndex"/> if missing or corrupt.</summary>
    public static EventsIndex LoadEventsIndex()
    {
        if (!File.Exists(EventsIndexPath)) return new EventsIndex();
        try { return JsonSerializer.Deserialize<EventsIndex>(File.ReadAllText(EventsIndexPath)) ?? new EventsIndex(); }
        catch { return new EventsIndex(); }
    }

    /// <summary>Persists the calendar events index to disk.</summary>
    public static void SaveEventsIndex(EventsIndex idx) =>
        File.WriteAllText(EventsIndexPath, JsonSerializer.Serialize(idx, JsonOpts));

    /// <summary>Loads the cached event at <paramref name="relativePath"/> (relative to <see cref="CacheRoot"/>). Returns null if missing.</summary>
    public static JsonObject? LoadEvent(string relativePath)
    {
        var full = Path.Combine(CacheRoot, relativePath);
        if (!File.Exists(full)) return null;
        return JsonNode.Parse(File.ReadAllText(full))?.AsObject();
    }
}

/// <summary>Per-folder sync state persisted to <c>state.json</c>.</summary>
public class State
{
    /// <summary>Map from folder alias (inbox, sentitems, etc.) to its sync state.</summary>
    public Dictionary<string, FolderState> Folders { get; set; } = new();
}

/// <summary>Sync state for a single folder.</summary>
public class FolderState
{
    /// <summary>Graph delta token for incremental sync continuation.</summary>
    public string? DeltaLink { get; set; }
    /// <summary>Timestamp of the most recent successful sync of this folder.</summary>
    public DateTimeOffset? LastSync { get; set; }
    /// <summary>Cached number of messages in this folder (used for status reporting).</summary>
    public int MessageCount { get; set; }
}

/// <summary>Lookup index for the message cache, persisted to <c>index.json</c>.</summary>
public class Index
{
    /// <summary>Map from message id → relative path under <see cref="Storage.CacheRoot"/>.</summary>
    public Dictionary<string, string> ById { get; set; } = new();
    /// <summary>Map from conversation id → list of message ids in that conversation.</summary>
    public Dictionary<string, List<string>> ByConversation { get; set; } = new();
}

/// <summary>Lookup index for the calendar events cache, persisted to <c>events_index.json</c>.</summary>
public class EventsIndex
{
    /// <summary>Map from event id → relative path under <see cref="Storage.CacheRoot"/>.</summary>
    public Dictionary<string, string> ById { get; set; } = new();
    /// <summary>Lower bound of the synced calendar window.</summary>
    public DateTimeOffset? WindowStart { get; set; }
    /// <summary>Upper bound of the synced calendar window.</summary>
    public DateTimeOffset? WindowEnd { get; set; }
    /// <summary>Timestamp of the most recent successful calendar sync.</summary>
    public DateTimeOffset? LastSync { get; set; }
}
