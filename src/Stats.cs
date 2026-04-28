using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailTool;

/// <summary>Aggregates the local cache: top senders, top domains, monthly distribution.</summary>
public static class Stats
{
    /// <summary>
    /// Produces aggregates over the cache honoring <paramref name="opts"/> filters
    /// (<c>--in-folder</c>, <c>--since</c>, <c>--until</c>, <c>--from</c>, <c>--subject</c>, etc).
    /// Emits human-readable tables by default, or JSON when <c>--json</c> is set.
    /// </summary>
    public static void Run(SearchOptions opts)
    {
        if (!string.IsNullOrEmpty(opts.SubjectRegex))
            // 1s match timeout: defend against ReDoS via hostile regex patterns.
            opts.SubjectRegexCompiled = new System.Text.RegularExpressions.Regex(
                opts.SubjectRegex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
                TimeSpan.FromSeconds(1));

        var senders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var domains = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byMonth = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int total = 0;

        var index = Storage.LoadIndex();
        foreach (var (_, rel) in index.ById)
        {
            var msg = Storage.LoadMessage(rel);
            if (msg is null) continue;
            if (!Search.Matches(msg, opts)) continue;
            total++;

            var addr = msg["from"]?["address"]?.GetValue<string>() ?? "unknown";
            addr = addr.ToLowerInvariant();
            senders[addr] = senders.GetValueOrDefault(addr) + 1;

            if (addr.Contains('@'))
            {
                var dom = addr[(addr.IndexOf('@') + 1)..];
                domains[dom] = domains.GetValueOrDefault(dom) + 1;
            }

            var dt = msg["receivedDateTime"]?.GetValue<string>() ?? "";
            if (dt.Length >= 7)
            {
                var mo = dt[..7];
                byMonth[mo] = byMonth.GetValueOrDefault(mo) + 1;
            }
        }

        var topSenders = senders.OrderByDescending(kv => kv.Value).Take(50).ToList();
        var topDomains = domains.OrderByDescending(kv => kv.Value).Take(30).ToList();

        if (opts.Json)
        {
            var obj = new JsonObject
            {
                ["total"] = total,
                ["senders"] = new JsonArray(topSenders.Select(kv =>
                    (JsonNode?)new JsonObject { ["address"] = kv.Key, ["count"] = kv.Value }).ToArray()),
                ["domains"] = new JsonArray(topDomains.Select(kv =>
                    (JsonNode?)new JsonObject { ["domain"] = kv.Key, ["count"] = kv.Value }).ToArray()),
                ["by_month"] = new JsonObject(byMonth.Select(kv =>
                    new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value)).ToArray())
            };
            Console.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Console.WriteLine($"Total messages matched: {total}");
        Console.WriteLine();
        Console.WriteLine("--- Top 50 senders ---");
        foreach (var (addr, n) in topSenders)
            Console.WriteLine($"  {n,5}  {addr}");
        Console.WriteLine();
        Console.WriteLine("--- Top 30 domains ---");
        foreach (var (dom, n) in topDomains)
            Console.WriteLine($"  {n,5}  {dom}");
        Console.WriteLine();
        Console.WriteLine("--- By month ---");
        foreach (var (mo, n) in byMonth)
            Console.WriteLine($"  {mo}  {n}");
    }
}
