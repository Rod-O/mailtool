using System.Text.Json.Nodes;
using Microsoft.Graph;

namespace MailTool;

/// <summary>
/// Pages backwards from the oldest cached message to fetch older history.
/// Used after the initial delta sync to extend the local cache further into
/// the past, since delta sync only covers messages received after the first
/// fetch.
/// </summary>
public static class Backfill
{
    /// <summary>
    /// Pages backwards through the named folders, fetching <paramref name="pages"/>
    /// pages of older messages and storing them in the local cache.
    /// </summary>
    public static async Task RunAsync(string[] folders, int pages, CancellationToken ct)
    {
        Storage.EnsureDirs();

        var oldest = FindOldest();
        if (oldest is null)
        {
            Console.Error.WriteLine("No messages in cache. Run sync first.");
            return;
        }

        Console.Error.WriteLine($"Oldest in cache: {oldest.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC");

        var client = await Auth.GetClientAsync(ct);
        var index = Storage.LoadIndex();

        foreach (var folder in folders)
            await BackfillFolderAsync(client, folder, oldest.Value, pages, index, ct);

        Storage.SaveIndex(index);
        Console.Error.WriteLine("Backfill complete.");
    }

    private static DateTimeOffset? FindOldest()
    {
        DateTimeOffset? oldest = null;
        foreach (var file in Storage.EnumerateMessageFiles())
        {
            try
            {
                var dt = JsonNode.Parse(File.ReadAllText(file))?["receivedDateTime"]?.GetValue<string>();
                if (dt is null) continue;
                var parsed = DateTimeOffset.Parse(dt);
                if (oldest is null || parsed < oldest) oldest = parsed;
            }
            catch { }
        }
        return oldest;
    }

    private static async Task BackfillFolderAsync(
        GraphServiceClient client,
        string folder,
        DateTimeOffset before,
        int pages,
        Index index,
        CancellationToken ct)
    {
        Console.Error.WriteLine($"Backfilling folder: {folder}");

        var filter = $"receivedDateTime lt {before.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";

        var response = await client.Me.MailFolders[folder].Messages.GetAsync(cfg =>
        {
            cfg.QueryParameters.Select = Sync.SelectFields;
            cfg.QueryParameters.Orderby = ["receivedDateTime desc"];
            cfg.QueryParameters.Filter = filter;
            cfg.QueryParameters.Top = 50;
        }, cancellationToken: ct);

        int added = 0, pageCount = 0;

        while (response is not null && pageCount < pages)
        {
            pageCount++;
            foreach (var msg in response.Value ?? [])
            {
                if (msg.Id is null || index.ById.ContainsKey(msg.Id)) continue;
                Sync.WriteMessage(msg, index);
                added++;
            }

            response = !string.IsNullOrEmpty(response.OdataNextLink) && pageCount < pages
                ? await client.Me.MailFolders[folder].Messages
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: ct)
                : null;
        }

        Console.Error.WriteLine($"  {folder}: +{added} ({pageCount} pages)");
    }
}
