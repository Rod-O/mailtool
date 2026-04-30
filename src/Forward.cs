using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>Forwards an existing message to new recipients, with optional additional attachments.</summary>
public static class Forward
{
    /// <summary>
    /// Creates a forward draft via <c>createForward</c>, attaches any files, then sends.
    /// Supports multiple <c>--to</c> recipients and multiple <c>--attach</c> files.
    /// </summary>
    public static async Task RunAsync(
        string messageId,
        string[] to,
        string body,
        string[] attachments,
        bool autoYes,
        CancellationToken ct)
    {
        if (to.Length == 0)
        {
            Console.Error.WriteLine("At least one --to recipient is required.");
            Environment.Exit(2);
            return;
        }

        var client = await Auth.GetClientAsync(ct);
        var index  = Storage.LoadIndex();

        var fullId = Helpers.ResolveId(messageId, index);
        if (fullId is null)
        {
            Console.Error.WriteLine($"Message not found: {messageId}");
            Environment.Exit(1);
            return;
        }

        var origRel = index.ById.TryGetValue(fullId, out var rel) ? rel : null;
        var orig = origRel is null ? null : Storage.LoadMessage(origRel);
        var origSubject = orig?["subject"]?.GetValue<string>() ?? "(no subject)";

        if (Confirm.Email("forward", to, Array.Empty<string>(), "Fw: " + origSubject, body, autoYes) == Confirm.Outcome.Cancel)
        {
            Console.Error.WriteLine("Cancelled — forward not sent.");
            Environment.Exit(1);
            return;
        }

        var recipients = to.Select(addr => new Recipient
        {
            EmailAddress = new EmailAddress { Address = addr }
        }).ToList();

        var requestBody = new Microsoft.Graph.Me.Messages.Item.CreateForward.CreateForwardPostRequestBody
        {
            ToRecipients = recipients,
            Message = string.IsNullOrWhiteSpace(body)
                ? null
                : new Message { Body = new ItemBody { ContentType = BodyType.Html, Content = body.Replace("\n", "<br>") } }
        };

        var draft = await client.Me.Messages[fullId].CreateForward.PostAsync(requestBody, cancellationToken: ct);
        if (draft?.Id is null)
        {
            Console.Error.WriteLine("Failed to create forward draft.");
            Environment.Exit(1);
            return;
        }

        Console.Error.WriteLine($"Draft created: {draft.Id[..20]}...");
        await Helpers.AttachFilesAsync(client, draft.Id, attachments, ct);
        await client.Me.Messages[draft.Id].Send.PostAsync(cancellationToken: ct);
        Console.Error.WriteLine($"Forwarded to: {string.Join(", ", to)}");
    }
}
