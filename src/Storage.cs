using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailTool;

public static class Storage
{
    public static readonly string CacheRoot =
        Environment.GetEnvironmentVariable("MAILTOOL_CACHE")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mailtool", "cache");
    public static readonly string MessagesDir = Path.Combine(CacheRoot, "messages");
    public static readonly string ThreadsDir = Path.Combine(CacheRoot, "threads");
    public static readonly string EventsDir = Path.Combine(CacheRoot, "events");
    public static readonly string StatePath = Path.Combine(CacheRoot, "state.json");
    public static readonly string IndexPath = Path.Combine(CacheRoot, "index.json");
    public static readonly string EventsIndexPath = Path.Combine(CacheRoot, "events_index.json");

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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

    public static string MessagePath(DateTimeOffset received, string id)
    {
        var folder = Path.Combine(MessagesDir, received.Year.ToString("D4"), received.Month.ToString("D2"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SanitizeId(id) + ".json");
    }

    public static string ThreadPath(string conversationId) =>
        Path.Combine(ThreadsDir, SanitizeId(conversationId) + ".json");

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

    public static void SaveState(State state) =>
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOpts));

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

    public static void SaveIndex(Index index) =>
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOpts));

    public static JsonObject? LoadMessage(string relativePath)
    {
        var full = Path.Combine(CacheRoot, relativePath);
        if (!File.Exists(full)) return null;
        return JsonNode.Parse(File.ReadAllText(full))?.AsObject();
    }

    public static IEnumerable<string> EnumerateMessageFiles() =>
        Directory.Exists(MessagesDir)
            ? Directory.EnumerateFiles(MessagesDir, "*.json", SearchOption.AllDirectories)
            : [];

    public static string EventPath(DateTimeOffset start, string id)
    {
        var folder = Path.Combine(EventsDir, start.Year.ToString("D4"), start.Month.ToString("D2"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SanitizeId(id) + ".json");
    }

    public static EventsIndex LoadEventsIndex()
    {
        if (!File.Exists(EventsIndexPath)) return new EventsIndex();
        try { return JsonSerializer.Deserialize<EventsIndex>(File.ReadAllText(EventsIndexPath)) ?? new EventsIndex(); }
        catch { return new EventsIndex(); }
    }

    public static void SaveEventsIndex(EventsIndex idx) =>
        File.WriteAllText(EventsIndexPath, JsonSerializer.Serialize(idx, JsonOpts));

    public static JsonObject? LoadEvent(string relativePath)
    {
        var full = Path.Combine(CacheRoot, relativePath);
        if (!File.Exists(full)) return null;
        return JsonNode.Parse(File.ReadAllText(full))?.AsObject();
    }
}

public class State
{
    public Dictionary<string, FolderState> Folders { get; set; } = new();
}

public class FolderState
{
    public string? DeltaLink { get; set; }
    public DateTimeOffset? LastSync { get; set; }
    public int MessageCount { get; set; }
}

public class Index
{
    public Dictionary<string, string> ById { get; set; } = new();
    public Dictionary<string, List<string>> ByConversation { get; set; } = new();
}

public class EventsIndex
{
    public Dictionary<string, string> ById { get; set; } = new();
    public DateTimeOffset? WindowStart { get; set; }
    public DateTimeOffset? WindowEnd { get; set; }
    public DateTimeOffset? LastSync { get; set; }
}
