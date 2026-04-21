using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>Creates a new draft message in the Drafts folder without sending it.</summary>
public static class Draft
{
    /// <summary>
    /// Creates a draft and optionally attaches files. Prints the draft id to stdout on success
    /// so it can be captured and passed to <c>reply</c>, <c>forward</c>, or opened in Outlook.
    /// </summary>
    public static async Task RunAsync(
        string[] to,
        string[] cc,
        string subject,
        string body,
        string[] attachments,
        CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);

        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody { ContentType = BodyType.Html, Content = body.Replace("\n", "<br>") },
            ToRecipients = to.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } }).ToList(),
            CcRecipients = cc.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } }).ToList()
        };

        var draft = await client.Me.Messages.PostAsync(message, cancellationToken: ct);
        if (draft?.Id is null)
        {
            Console.Error.WriteLine("Failed to create draft.");
            Environment.Exit(1);
            return;
        }

        if (attachments.Length > 0)
            await Helpers.AttachFilesAsync(client, draft.Id, attachments, ct);

        Console.WriteLine(draft.Id);
        Console.Error.WriteLine($"Draft saved: {draft.Id[..20]}...");
    }
}
