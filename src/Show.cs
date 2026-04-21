using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MailTool;

public static class Show
{
    public static void Run(string id, bool rawBody = false)
    {
        var index = Storage.LoadIndex();
        if (!index.ById.TryGetValue(id, out var rel))
        {
            // Try partial match — user may paste a prefix
            var match = index.ById.Keys.FirstOrDefault(k => k.StartsWith(id, StringComparison.Ordinal));
            if (match is null)
            {
                Console.Error.WriteLine($"Message not found: {id}");
                Environment.ExitCode = 1;
                return;
            }
            rel = index.ById[match];
        }

        var msg = Storage.LoadMessage(rel);
        if (msg is null)
        {
            Console.Error.WriteLine($"Message file missing: {rel}");
            Environment.ExitCode = 1;
            return;
        }

        RenderMessage(msg, rawBody);
    }

    public static void RenderMessage(JsonObject msg, bool rawBody = false)
    {
        var subject = msg["subject"]?.GetValue<string>() ?? "(no subject)";
        var from = FormatAddress(msg["from"]);
        var to = FormatAddressList(msg["to"]);
        var cc = FormatAddressList(msg["cc"]);
        var received = msg["receivedDateTime"]?.GetValue<string>();

        Console.WriteLine($"From:    {from}");
        Console.WriteLine($"To:      {to}");
        if (!string.IsNullOrEmpty(cc)) Console.WriteLine($"Cc:      {cc}");
        Console.WriteLine($"Date:    {received}");
        Console.WriteLine($"Subject: {subject}");
        if (msg["hasAttachments"]?.GetValue<bool>() == true)
            Console.WriteLine("Attachments: yes (use `mailtool attach <id>` — v2)");
        Console.WriteLine(new string('─', 72));

        var body = msg["body"]?["content"]?.GetValue<string>() ?? msg["bodyPreview"]?.GetValue<string>() ?? "";
        var contentType = msg["body"]?["contentType"]?.GetValue<string>();

        if (!rawBody && string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase))
            body = HtmlToText(body);

        Console.WriteLine(body);
    }

    public static string FormatAddress(JsonNode? node)
    {
        if (node is null) return "";
        var name = node["name"]?.GetValue<string>();
        var addr = node["address"]?.GetValue<string>() ?? "";
        return string.IsNullOrEmpty(name) ? addr : $"{name} <{addr}>";
    }

    public static string FormatAddressList(JsonNode? node)
    {
        if (node is not JsonArray arr) return "";
        return string.Join(", ", arr.Select(FormatAddress).Where(s => !string.IsNullOrEmpty(s)));
    }

    private static readonly Regex ScriptRe = new(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex StyleRe = new(@"<style[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TagRe = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WsRe = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex BlankLineRe = new(@"\n{3,}", RegexOptions.Compiled);

    public static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = ScriptRe.Replace(html, "");
        s = StyleRe.Replace(s, "");
        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</(p|div|li|tr|h[1-6])>", "\n", RegexOptions.IgnoreCase);
        s = TagRe.Replace(s, "");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = WsRe.Replace(s, " ");
        s = BlankLineRe.Replace(s, "\n\n");
        return s.Trim();
    }
}
