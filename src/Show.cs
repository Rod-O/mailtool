using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MailTool;

/// <summary>
/// Prints a single cached message to stdout — headers (From/To/Cc/Date/Subject)
/// followed by a separator and the body. HTML bodies are converted to plain
/// text by default; use <c>rawBody=true</c> to preserve raw HTML.
/// </summary>
public static class Show
{
    /// <summary>
    /// Loads the message identified by <paramref name="id"/> from the cache and
    /// renders it. <paramref name="id"/> may be a full Graph id or a unique prefix.
    /// </summary>
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

    /// <summary>
    /// Renders a message JSON object in the standard headers-then-body layout.
    /// Sanitizes all sender-controlled strings to strip terminal control sequences.
    /// </summary>
    public static void RenderMessage(JsonObject msg, bool rawBody = false)
    {
        // Every value below originates from a remote sender — strip terminal
        // control characters before writing so a hostile email can't inject
        // ANSI/OSC sequences into the operator's terminal.
        var subject = SanitizeForTerminal(msg["subject"]?.GetValue<string>() ?? "(no subject)");
        var from = SanitizeForTerminal(FormatAddress(msg["from"]));
        var to = SanitizeForTerminal(FormatAddressList(msg["to"]));
        var cc = SanitizeForTerminal(FormatAddressList(msg["cc"]));
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

        Console.WriteLine(SanitizeForTerminal(body));
    }

    /// <summary>
    /// Formats a single Graph email-address node as <c>"Name &lt;addr&gt;"</c>,
    /// or just the address if the name is missing.
    /// </summary>
    public static string FormatAddress(JsonNode? node)
    {
        if (node is null) return "";
        var name = node["name"]?.GetValue<string>();
        var addr = node["address"]?.GetValue<string>() ?? "";
        return string.IsNullOrEmpty(name) ? addr : $"{name} <{addr}>";
    }

    /// <summary>Formats a Graph email-address array as a comma-separated list of <see cref="FormatAddress"/> entries.</summary>
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

    /// <summary>
    /// Matches C0 control characters that should never reach the terminal:
    /// 0x00–0x08, 0x0B, 0x0C, 0x0E–0x1F, and 0x7F (DEL). Excludes 0x09 (tab),
    /// 0x0A (LF), and 0x0D (CR), which are legitimate in email bodies.
    /// Stripping 0x1B (ESC) defangs every ANSI escape sequence — without ESC
    /// the remainder of the sequence is just printable garbage.
    /// </summary>
    private static readonly Regex ControlCharsRe =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    /// <summary>
    /// Strips terminal control characters from untrusted email content before
    /// printing to the user's terminal. Defends against ANSI/OSC escape
    /// injection from a hostile sender (e.g. a phishing email crafted with
    /// cursor manipulation, screen clearing, or clipboard-write OSC52).
    /// </summary>
    public static string SanitizeForTerminal(string s) =>
        string.IsNullOrEmpty(s) ? "" : ControlCharsRe.Replace(s, "");

    /// <summary>
    /// Best-effort HTML → plain-text conversion: drops script/style blocks,
    /// replaces block-level closers with newlines, strips remaining tags,
    /// decodes HTML entities, and collapses whitespace.
    /// </summary>
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
