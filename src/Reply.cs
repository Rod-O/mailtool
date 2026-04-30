using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>Sends a reply (or reply-all) to an existing message, with optional attachments.</summary>
public static class Reply
{
    /// <summary>
    /// Creates a reply draft via <c>createReply</c> / <c>createReplyAll</c>, attaches any files,
    /// then sends. This three-step flow is required because the reply actions do not accept
    /// inline attachments.
    /// </summary>
    public static async Task RunAsync(
        string messageId,
        string body,
        bool replyAll,
        string[] attachments,
        bool autoYes,
        CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var index  = Storage.LoadIndex();

        var fullId = Helpers.ResolveId(messageId, index);
        if (fullId is null)
        {
            Console.Error.WriteLine($"Message not found: {messageId}");
            Environment.Exit(1);
            return;
        }

        // Pull the original from cache so the confirmation preview can show the
        // user who they're actually replying to. Reply recipients are filled in
        // server-side by createReply / createReplyAll, but we mirror them locally
        // for the preview.
        var origRel = index.ById.TryGetValue(fullId, out var rel) ? rel : null;
        var orig = origRel is null ? null : Storage.LoadMessage(origRel);
        var origFromAddr = orig?["from"]?["address"]?.GetValue<string>() ?? "";
        var origSubject  = orig?["subject"]?.GetValue<string>() ?? "(no subject)";
        var replyTo = string.IsNullOrEmpty(origFromAddr) ? Array.Empty<string>() : new[] { origFromAddr };
        var replyCc = Array.Empty<string>();
        if (replyAll && orig?["to"] is System.Text.Json.Nodes.JsonArray toArr)
        {
            // reply-all: surface other recipients to make blast radius visible.
            replyCc = toArr
                .Select(n => n?["address"]?.GetValue<string>() ?? "")
                .Where(s => !string.IsNullOrEmpty(s) && !string.Equals(s, origFromAddr, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (Confirm.Email(replyAll ? "reply-all" : "reply", replyTo, replyCc, "Re: " + origSubject, body, autoYes) == Confirm.Outcome.Cancel)
        {
            Console.Error.WriteLine("Cancelled — reply not sent.");
            Environment.Exit(1);
            return;
        }

        var payload = new Message
        {
            Body = new ItemBody { ContentType = BodyType.Html, Content = body.Replace("\n", "<br>") }
        };

        Message? draft;
        if (replyAll)
            draft = await client.Me.Messages[fullId].CreateReplyAll.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.CreateReplyAll.CreateReplyAllPostRequestBody { Message = payload },
                cancellationToken: ct);
        else
            draft = await client.Me.Messages[fullId].CreateReply.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.CreateReply.CreateReplyPostRequestBody { Message = payload },
                cancellationToken: ct);

        if (draft?.Id is null)
        {
            Console.Error.WriteLine("Failed to create reply draft.");
            Environment.Exit(1);
            return;
        }

        Console.Error.WriteLine($"Draft created: {draft.Id[..20]}...");
        await Helpers.AttachFilesAsync(client, draft.Id, attachments, ct);
        await client.Me.Messages[draft.Id].Send.PostAsync(cancellationToken: ct);
        Console.Error.WriteLine($"Sent ({(replyAll ? "reply-all" : "reply")}).");
    }
}
