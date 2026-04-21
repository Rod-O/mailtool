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
    public static readonly string StatePath = Path.Combine(CacheRoot, "state.json");
    public static readonly string IndexPath = Path.Combine(CacheRoot, "index.json");

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

    public static string SanitizeId(string id) =>
        id.Replace('/', '_').Replace('+', '-').TrimEnd('=');

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
