using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>Sends a new outbound email message.</summary>
public static class Send
{
    /// <summary>
    /// Composes and sends a new message. If attachments are provided, creates a draft first,
    /// attaches files, then sends — because <c>sendMail</c> does not support inline attachments.
    /// </summary>
    public static async Task RunAsync(
        string[] to,
        string[] cc,
        string subject,
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

        if (Confirm.Email("send", to, cc, subject, body, autoYes) == Confirm.Outcome.Cancel)
        {
            Console.Error.WriteLine("Cancelled — message not sent.");
            Environment.Exit(1);
            return;
        }

        var client = await Auth.GetClientAsync(ct);

        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody { ContentType = BodyType.Html, Content = body.Replace("\n", "<br>") },
            ToRecipients = to.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } }).ToList(),
            CcRecipients = cc.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } }).ToList()
        };

        if (attachments.Length == 0)
        {
            await client.Me.SendMail.PostAsync(
                new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                },
                cancellationToken: ct);
        }
        else
        {
            // Graph sendMail does not accept attachments — create draft, attach, send.
            var draft = await client.Me.Messages.PostAsync(message, cancellationToken: ct);
            if (draft?.Id is null)
            {
                Console.Error.WriteLine("Failed to create draft.");
                Environment.Exit(1);
                return;
            }
            Console.Error.WriteLine($"Draft created: {draft.Id[..20]}...");
            await Helpers.AttachFilesAsync(client, draft.Id, attachments, ct);
            await client.Me.Messages[draft.Id].Send.PostAsync(cancellationToken: ct);
        }

        Console.Error.WriteLine($"Sent to: {string.Join(", ", to)}");
    }
}
