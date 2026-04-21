using System.Text.Json.Nodes;

namespace MailTool;

public class SearchOptions
{
    public string? Query { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset? Since { get; set; }
    public DateTimeOffset? Until { get; set; }
    public int Limit { get; set; } = 50;
    public bool BodyMatch { get; set; } = false;
}

public static class Search
{
    public static void Run(SearchOptions opts)
    {
        var index = Storage.LoadIndex();
        var results = new List<(DateTimeOffset received, JsonObject msg, string rel)>();

        foreach (var (id, rel) in index.ById)
        {
            var msg = Storage.LoadMessage(rel);
            if (msg is null) continue;

            if (!Matches(msg, opts)) continue;

            var received = ParseDate(msg["receivedDateTime"]?.GetValue<string>());
            results.Add((received, msg, rel));
        }

        foreach (var (received, msg, rel) in results
            .OrderByDescending(r => r.received)
            .Take(opts.Limit))
        {
            var from = msg["from"]?["address"]?.GetValue<string>() ?? "(no-from)";
            var subject = msg["subject"]?.GetValue<string>() ?? "(no-subject)";
            var id = msg["id"]?.GetValue<string>() ?? "";
            var flagRead = msg["isRead"]?.GetValue<bool>() == true ? " " : "•";
            var attach = msg["hasAttachments"]?.GetValue<bool>() == true ? "📎" : "  ";
            Console.WriteLine($"{flagRead} {attach} {received:yyyy-MM-dd HH:mm}  {Truncate(from, 32),-32}  {Truncate(subject, 70)}");
            Console.WriteLine($"      id: {id}");
        }

        Console.Error.WriteLine($"--- {Math.Min(results.Count, opts.Limit)} of {results.Count} match(es) ---");
    }

    internal static bool Matches(JsonObject msg, SearchOptions opts)
    {
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

    private static DateTimeOffset ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, out var dt) ? dt : DateTimeOffset.MinValue;

    private static string Truncate(string s, int len) =>
        s.Length <= len ? s : s[..(len - 1)] + "…";
}
