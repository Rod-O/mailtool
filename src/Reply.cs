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
