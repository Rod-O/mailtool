using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Me.MailFolders.Item.Messages.Delta;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>
/// Incremental delta sync of Microsoft Graph mail folders into the local cache.
/// Persists a delta token per folder so subsequent runs only fetch new and
/// changed messages since the last sync.
/// </summary>
public static class Sync
{
    internal static readonly string[] SelectFields =
    [
        "id", "internetMessageId", "subject", "from", "sender",
        "toRecipients", "ccRecipients", "bccRecipients", "replyTo",
        "receivedDateTime", "sentDateTime",
        "bodyPreview", "body",
        "conversationId", "conversationIndex",
        "hasAttachments", "importance", "isRead", "isDraft",
        "flag", "categories", "webLink", "parentFolderId"
    ];

    /// <summary>
    /// Runs a delta sync on the named folders, writing new/updated messages
    /// to the cache and saving the resulting delta token for the next run.
    /// </summary>
    public static async Task RunAsync(string[] folders, CancellationToken ct)
    {
        Storage.EnsureDirs();
        var client = await Auth.GetClientAsync(ct);
        var state = Storage.LoadState();
        var index = Storage.LoadIndex();

        foreach (var folder in folders)
        {
            Console.Error.WriteLine($"Syncing folder: {folder}");
            await SyncFolderAsync(client, folder, state, index, ct);
        }

        Storage.SaveState(state);
        Storage.SaveIndex(index);
        Console.Error.WriteLine("Sync complete.");
    }

    private static async Task SyncFolderAsync(
        GraphServiceClient client,
        string folder,
        State state,
        Index index,
        CancellationToken ct)
    {
        if (!state.Folders.TryGetValue(folder, out var fs))
        {
            fs = new FolderState();
            state.Folders[folder] = fs;
        }

        DeltaGetResponse? page;

        if (!string.IsNullOrEmpty(fs.DeltaLink))
        {
            page = await client.Me.MailFolders[folder].Messages.Delta
                .WithUrl(fs.DeltaLink)
                .GetAsDeltaGetResponseAsync(cancellationToken: ct);
        }
        else
        {
            page = await client.Me.MailFolders[folder].Messages.Delta
                .GetAsDeltaGetResponseAsync(cfg =>
                {
                    cfg.QueryParameters.Select = SelectFields;
                    cfg.QueryParameters.Top = 50;
                }, cancellationToken: ct);
        }

        int added = 0, updated = 0, removed = 0, pages = 0;

        while (page is not null)
        {
            pages++;
            if (page.Value is not null)
            {
                foreach (var msg in page.Value)
                {
                    if (msg.Id is null) continue;

                    if (msg.AdditionalData.TryGetValue("@removed", out _))
                    {
                        if (RemoveMessage(msg.Id, index))
                            removed++;
                        continue;
                    }

                    var wasPresent = index.ById.ContainsKey(msg.Id);
                    WriteMessage(msg, index);
                    if (wasPresent) updated++; else added++;
                }
            }

            if (!string.IsNullOrEmpty(page.OdataNextLink))
            {
                page = await client.Me.MailFolders[folder].Messages.Delta
                    .WithUrl(page.OdataNextLink)
                    .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }
            else
            {
                if (!string.IsNullOrEmpty(page.OdataDeltaLink))
                    fs.DeltaLink = page.OdataDeltaLink;
                page = null;
            }
        }

        fs.LastSync = DateTimeOffset.UtcNow;
        fs.MessageCount = index.ById.Values.Count(path => path.Contains(folder, StringComparison.OrdinalIgnoreCase) || true);
        Console.Error.WriteLine($"  {folder}: +{added} ~{updated} -{removed} ({pages} pages)");
    }

    internal static void WriteMessage(Message msg, Index index)
    {
        var received = msg.ReceivedDateTime ?? msg.SentDateTime ?? DateTimeOffset.UtcNow;
        var path = Storage.MessagePath(received, msg.Id!);
        var rel = Path.GetRelativePath(Storage.CacheRoot, path);

        var json = SerializeMessage(msg);
        File.WriteAllText(path, json);

        index.ById[msg.Id!] = rel;

        if (!string.IsNullOrEmpty(msg.ConversationId))
        {
            if (!index.ByConversation.TryGetValue(msg.ConversationId, out var list))
            {
                list = new List<string>();
                index.ByConversation[msg.ConversationId] = list;
            }
            if (!list.Contains(msg.Id!)) list.Add(msg.Id!);
        }
    }

    private static bool RemoveMessage(string id, Index index)
    {
        if (!index.ById.TryGetValue(id, out var rel)) return false;
        var full = Path.Combine(Storage.CacheRoot, rel);
        if (File.Exists(full)) File.Delete(full);
        index.ById.Remove(id);
        foreach (var list in index.ByConversation.Values) list.Remove(id);
        return true;
    }

    private static string SerializeMessage(Message msg)
    {
        var obj = new Dictionary<string, object?>
        {
            ["id"] = msg.Id,
            ["internetMessageId"] = msg.InternetMessageId,
            ["subject"] = msg.Subject,
            ["from"] = Addr(msg.From),
            ["sender"] = Addr(msg.Sender),
            ["to"] = msg.ToRecipients?.Select(Addr).ToArray(),
            ["cc"] = msg.CcRecipients?.Select(Addr).ToArray(),
            ["bcc"] = msg.BccRecipients?.Select(Addr).ToArray(),
            ["replyTo"] = msg.ReplyTo?.Select(Addr).ToArray(),
            ["receivedDateTime"] = msg.ReceivedDateTime?.ToString("o"),
            ["sentDateTime"] = msg.SentDateTime?.ToString("o"),
            ["bodyPreview"] = msg.BodyPreview,
            ["body"] = msg.Body is null ? null : new
            {
                contentType = msg.Body.ContentType?.ToString(),
                content = msg.Body.Content
            },
            ["conversationId"] = msg.ConversationId,
            ["conversationIndex"] = msg.ConversationIndex is null ? null : Convert.ToBase64String(msg.ConversationIndex),
            ["hasAttachments"] = msg.HasAttachments,
            ["importance"] = msg.Importance?.ToString(),
            ["isRead"] = msg.IsRead,
            ["isDraft"] = msg.IsDraft,
            ["categories"] = msg.Categories,
            ["webLink"] = msg.WebLink,
            ["parentFolderId"] = msg.ParentFolderId,
            ["flag"] = msg.Flag is null ? null : new { status = msg.Flag.FlagStatus?.ToString() }
        };
        return JsonSerializer.Serialize(obj, Storage.JsonOpts);
    }

    private static object? Addr(Recipient? r) =>
        r?.EmailAddress is null ? null : new { name = r.EmailAddress.Name, address = r.EmailAddress.Address };
}
