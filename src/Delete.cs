using Microsoft.Graph;

namespace MailTool;

/// <summary>Moves messages to Deleted Items.</summary>
public static class Delete
{
    /// <summary>
    /// Deletes one or more messages by id or id-prefix.
    /// Graph DELETE on a message moves it to Deleted Items (not permanent).
    /// </summary>
    public static async Task RunAsync(string[] messageIds, CancellationToken ct)
    {
        if (messageIds.Length == 0)
        {
            Console.Error.WriteLine("Provide at least one message id.");
            Environment.Exit(2);
            return;
        }

        var client = await Auth.GetClientAsync(ct);
        var index  = Storage.LoadIndex();
        var errors = 0;

        foreach (var id in messageIds)
        {
            var fullId = Helpers.ResolveId(id, index);
            if (fullId is null)
            {
                Console.Error.WriteLine($"Message not found: {id}");
                errors++;
                continue;
            }

            await client.Me.Messages[fullId].DeleteAsync(cancellationToken: ct);
            Console.Error.WriteLine($"Deleted: {id}");
        }

        if (errors > 0) Environment.ExitCode = 1;
    }
}
