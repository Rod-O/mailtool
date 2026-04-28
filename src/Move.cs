using System.Text.Json.Nodes;
using Microsoft.Graph.Me.Messages.Item.Move;

namespace MailTool;

/// <summary>Moves messages to another mail folder by id or by selector (sender / subject regex / folder / date).</summary>
public static class Move
{
    /// <summary>
    /// Selects messages and moves them to <paramref name="destination"/>.
    /// Selection priority:
    ///   - If <paramref name="explicitIds"/> is non-empty, use those ids.
    ///   - Otherwise, evaluate <paramref name="selector"/> against the local cache.
    /// Destination accepts alias / display-name / "Parent/Child" path / raw Graph id.
    /// </summary>
    public static async Task RunAsync(
        string[] explicitIds,
        SearchOptions? selector,
        string destination,
        bool createIfMissing,
        bool dryRun,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            Console.Error.WriteLine("Provide a destination via --to <folder>.");
            Environment.Exit(2);
            return;
        }

        var client = await Auth.GetClientAsync(ct);
        var index = Storage.LoadIndex();

        // Resolve the source folder (if selector scopes to one) before pulling candidates.
        if (selector is not null && !string.IsNullOrEmpty(selector.InFolder))
        {
            var srcId = await Folders.ResolveAsync(client, selector.InFolder, create: false, ct);
            if (srcId is null)
            {
                Console.Error.WriteLine($"Source folder not found: {selector.InFolder}");
                Environment.Exit(1);
                return;
            }
            selector.InFolderId = srcId;
        }

        // Resolve destination. Do not create on dry-run; on a live run with --create, create if missing.
        var destId = await Folders.ResolveAsync(client, destination, create: false, ct);
        if (destId is null)
        {
            if (dryRun)
            {
                Console.Error.WriteLine($"[dry-run] Destination folder does not exist yet: {destination} (would be created on live run with --create).");
            }
            else if (createIfMissing)
            {
                destId = await Folders.ResolveAsync(client, destination, create: true, ct);
                if (destId is null)
                {
                    Console.Error.WriteLine($"Failed to create destination folder: {destination}");
                    Environment.Exit(1);
                    return;
                }
            }
            else
            {
                Console.Error.WriteLine($"Destination folder not found: {destination} (pass --create to create it).");
                Environment.Exit(1);
                return;
            }
        }

        // Build the id list.
        var ids = new List<string>();
        var previews = new List<string>();

        if (explicitIds.Length > 0)
        {
            foreach (var id in explicitIds)
            {
                var full = Helpers.ResolveId(id, index);
                if (full is null) { Console.Error.WriteLine($"Message not found: {id}"); continue; }
                ids.Add(full);
                previews.Add($"  {full[..Math.Min(20, full.Length)]}…");
            }
        }
        else if (selector is not null && HasAnyFilter(selector))
        {
            if (!string.IsNullOrEmpty(selector.SubjectRegex))
                // 1s match timeout: defend against ReDoS via hostile regex patterns.
                selector.SubjectRegexCompiled = new System.Text.RegularExpressions.Regex(
                    selector.SubjectRegex,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1));

            foreach (var (id, rel) in index.ById)
            {
                var msg = Storage.LoadMessage(rel);
                if (msg is null) continue;
                if (!Search.Matches(msg, selector)) continue;
                ids.Add(id);
                var subj = msg["subject"]?.GetValue<string>() ?? "";
                var from = msg["from"]?["address"]?.GetValue<string>() ?? "";
                var dt = (msg["receivedDateTime"]?.GetValue<string>() ?? "")[..Math.Min(10, (msg["receivedDateTime"]?.GetValue<string>() ?? "").Length)];
                previews.Add($"  {dt}  {Truncate(from, 32),-32}  {Truncate(subj, 70)}");
            }
        }
        else
        {
            Console.Error.WriteLine("No selection provided. Pass message ids, or a filter (--from / --subject-match / --in-folder / --since / --until).");
            Environment.Exit(2);
            return;
        }

        if (ids.Count == 0)
        {
            Console.Error.WriteLine("No messages matched.");
            return;
        }

        if (dryRun)
        {
            Console.Error.WriteLine($"[dry-run] would move {ids.Count} message(s) to: {destination}");
            foreach (var line in previews.Take(50)) Console.WriteLine(line);
            if (previews.Count > 50) Console.WriteLine($"  … and {previews.Count - 50} more");
            return;
        }

        Console.Error.WriteLine($"Moving {ids.Count} message(s) → {destination}");
        int moved = 0, errors = 0;
        foreach (var id in ids)
        {
            try
            {
                await client.Me.Messages[id].Move.PostAsync(
                    new MovePostRequestBody { DestinationId = destId },
                    cancellationToken: ct);
                moved++;
                if (moved % 25 == 0) Console.Error.WriteLine($"  {moved}/{ids.Count}…");
            }
            catch (Exception ex)
            {
                errors++;
                Console.Error.WriteLine($"  error on {id[..Math.Min(20, id.Length)]}…: {ex.Message}");
            }
        }
        Console.Error.WriteLine($"Moved: {moved}. Errors: {errors}.");
        if (errors > 0) Environment.ExitCode = 1;
    }

    private static bool HasAnyFilter(SearchOptions o) =>
        o.From is not null || o.To is not null
        || o.Subject is not null || o.SubjectRegex is not null
        || o.InFolderId is not null || o.Since is not null || o.Until is not null
        || o.Query is not null;

    private static string Truncate(string s, int len) =>
        s.Length <= len ? s : s[..(len - 1)] + "…";
}
