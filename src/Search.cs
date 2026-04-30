using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MailTool;

/// <summary>Search filter + output options shared across commands that select messages.</summary>
public class SearchOptions
{
    /// <summary>Free-text query — matched against subject, sender, and (optionally) body.</summary>
    public string? Query { get; set; }
    /// <summary>Substring filter on the sender address.</summary>
    public string? From { get; set; }
    /// <summary>Substring filter on the To recipients.</summary>
    public string? To { get; set; }
    /// <summary>Substring filter on the subject line.</summary>
    public string? Subject { get; set; }
    /// <summary>Regex filter on the subject line (more powerful than <see cref="Subject"/>).</summary>
    public string? SubjectRegex { get; set; }
    /// <summary>Lower bound on receivedDateTime (inclusive).</summary>
    public DateTimeOffset? Since { get; set; }
    /// <summary>Upper bound on receivedDateTime (exclusive).</summary>
    public DateTimeOffset? Until { get; set; }
    /// <summary>Maximum number of results to return. Default 50.</summary>
    public int Limit { get; set; } = 50;
    /// <summary>If true, also match <see cref="Query"/> against the message body, not just headers.</summary>
    public bool BodyMatch { get; set; } = false;
    /// <summary>User-supplied folder spec (alias, path, or raw id). Resolution to <see cref="InFolderId"/> is done by the caller before matching.</summary>
    public string? InFolder { get; set; }
    /// <summary>Resolved Graph folder id. Set by caller (Program.cs) after <see cref="Folders.ResolveAsync"/>.</summary>
    public string? InFolderId { get; set; }
    /// <summary>Emit JSON instead of the human-readable listing.</summary>
    public bool Json { get; set; } = false;

    internal Regex? SubjectRegexCompiled { get; set; }
}

/// <summary>Searches the local message cache using <see cref="SearchOptions"/> predicates.</summary>
public static class Search
{
    /// <summary>Runs the search and prints results in either human or JSON format.</summary>
    public static void Run(SearchOptions opts)
    {
        if (!string.IsNullOrEmpty(opts.SubjectRegex))
            // 1-second match timeout protects against ReDoS via hostile patterns
            // (e.g. (a+)+$). Subjects are short, but bodies can be large; without
            // the timeout a pathological pattern could hang the process.
            opts.SubjectRegexCompiled = new Regex(
                opts.SubjectRegex,
                RegexOptions.IgnoreCase | RegexOptions.Compiled,
                TimeSpan.FromSeconds(1));

        var index = Storage.LoadIndex();
        var results = new List<(DateTimeOffset received, JsonObject msg)>();

        foreach (var (id, rel) in index.ById)
        {
            var msg = Storage.LoadMessage(rel);
            if (msg is null) continue;
            if (!Matches(msg, opts)) continue;

            var received = ParseDate(msg["receivedDateTime"]?.GetValue<string>());
            results.Add((received, msg));
        }

        var ordered = results
            .OrderByDescending(r => r.received)
            .Take(opts.Limit)
            .ToList();

        if (opts.Json)
        {
            var arr = new JsonArray();
            foreach (var (_, msg) in ordered)
                arr.Add(ProjectForJson(msg));
            Console.WriteLine(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        }
        else
        {
            foreach (var (received, msg) in ordered)
            {
                // Strip terminal control chars from sender + subject — these are
                // attacker-controlled fields and must never reach the TTY raw.
                var from = Show.SanitizeForTerminal(msg["from"]?["address"]?.GetValue<string>() ?? "(no-from)");
                var subject = Show.SanitizeForTerminal(msg["subject"]?.GetValue<string>() ?? "(no-subject)");
                var id = msg["id"]?.GetValue<string>() ?? "";
                var flagRead = msg["isRead"]?.GetValue<bool>() == true ? " " : "•";
                var attach = msg["hasAttachments"]?.GetValue<bool>() == true ? "📎" : "  ";
                Console.WriteLine($"{flagRead} {attach} {received:yyyy-MM-dd HH:mm}  {Truncate(from, 32),-32}  {Truncate(subject, 70)}");
                Console.WriteLine($"      id: {id}");
            }
        }

        Console.Error.WriteLine($"--- {ordered.Count} of {results.Count} match(es) ---");
    }

    /// <summary>Applies <paramref name="opts"/> predicates against a cached message JSON object.</summary>
    internal static bool Matches(JsonObject msg, SearchOptions opts)
    {
        if (opts.InFolderId is not null)
        {
            var pfid = msg["parentFolderId"]?.GetValue<string>() ?? "";
            if (!string.Equals(pfid, opts.InFolderId, StringComparison.Ordinal))
                return false;
        }

        if (opts.From is not null)
        {
            var from = msg["from"]?["address"]?.GetValue<string>() ?? "";
            var fromName = msg["from"]?["name"]?.GetValue<string>() ?? "";
            if (!from.Contains(opts.From, StringComparison.OrdinalIgnoreCase) &&
                !fromName.Contains(opts.From, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (opts.To is not null)
        {
            var toMatch = false;
            if (msg["to"] is JsonArray toArr)
            {
                foreach (var r in toArr)
                {
                    var addr = r?["address"]?.GetValue<string>() ?? "";
                    var name = r?["name"]?.GetValue<string>() ?? "";
                    if (addr.Contains(opts.To, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(opts.To, StringComparison.OrdinalIgnoreCase))
                    {
                        toMatch = true;
                        break;
                    }
                }
            }
            if (!toMatch) return false;
        }

        if (opts.Subject is not null)
        {
            var subject = msg["subject"]?.GetValue<string>() ?? "";
            if (!subject.Contains(opts.Subject, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (opts.SubjectRegexCompiled is not null)
        {
            var subject = msg["subject"]?.GetValue<string>() ?? "";
            if (!opts.SubjectRegexCompiled.IsMatch(subject))
                return false;
        }

        var received = ParseDate(msg["receivedDateTime"]?.GetValue<string>());
        if (opts.Since is not null && received < opts.Since) return false;
        if (opts.Until is not null && received > opts.Until) return false;

        if (opts.Query is not null)
        {
            var subject = msg["subject"]?.GetValue<string>() ?? "";
            var preview = msg["bodyPreview"]?.GetValue<string>() ?? "";
            var body = opts.BodyMatch ? (msg["body"]?["content"]?.GetValue<string>() ?? "") : "";
            var from = msg["from"]?["address"]?.GetValue<string>() ?? "";
            var fromName = msg["from"]?["name"]?.GetValue<string>() ?? "";

            var haystack = string.Join("\n", subject, preview, body, from, fromName);
            if (!haystack.Contains(opts.Query, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>Returns a compact JSON projection of a cached message (no body, no recipients arrays).</summary>
    internal static JsonObject ProjectForJson(JsonObject msg)
    {
        var obj = new JsonObject
        {
            ["id"] = msg["id"]?.GetValue<string>(),
            ["receivedDateTime"] = msg["receivedDateTime"]?.GetValue<string>(),
            ["subject"] = msg["subject"]?.GetValue<string>(),
            ["from"] = msg["from"]?["address"]?.GetValue<string>(),
            ["fromName"] = msg["from"]?["name"]?.GetValue<string>(),
            ["conversationId"] = msg["conversationId"]?.GetValue<string>(),
            ["parentFolderId"] = msg["parentFolderId"]?.GetValue<string>(),
            ["hasAttachments"] = msg["hasAttachments"]?.GetValue<bool>() ?? false,
            ["isRead"] = msg["isRead"]?.GetValue<bool>() ?? false
        };
        return obj;
    }

    private static DateTimeOffset ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, out var dt) ? dt : DateTimeOffset.MinValue;

    private static string Truncate(string s, int len) =>
        s.Length <= len ? s : s[..(len - 1)] + "…";
}
