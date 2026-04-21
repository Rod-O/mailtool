using System.Text.Json.Nodes;

namespace MailTool;

public static class ThreadCmd
{
    public static void Run(string conversationIdOrMessageId, bool rawBody = false)
    {
        var index = Storage.LoadIndex();
        string? conversationId = conversationIdOrMessageId;

        if (!index.ByConversation.ContainsKey(conversationIdOrMessageId))
        {
            // Maybe user passed a message id; look up its conversationId
            if (index.ById.TryGetValue(conversationIdOrMessageId, out var rel))
            {
                var m = Storage.LoadMessage(rel);
                conversationId = m?["conversationId"]?.GetValue<string>();
            }
            else
            {
                // Try partial message id prefix
                var match = index.ById.Keys.FirstOrDefault(k => k.StartsWith(conversationIdOrMessageId, StringComparison.Ordinal));
                if (match is not null)
                {
                    var m = Storage.LoadMessage(index.ById[match]);
                    conversationId = m?["conversationId"]?.GetValue<string>();
                }
            }
        }

        if (conversationId is null || !index.ByConversation.TryGetValue(conversationId, out var messageIds))
        {
            Console.Error.WriteLine("Thread not found.");
            Environment.ExitCode = 1;
            return;
        }

        var messages = messageIds
            .Select(id => index.ById.TryGetValue(id, out var rel) ? Storage.LoadMessage(rel) : null)
            .Where(m => m is not null)
            .OrderBy(m => m!["receivedDateTime"]?.GetValue<string>())
            .ToList();

        Console.Error.WriteLine($"Thread: {conversationId}  ({messages.Count} message(s))");
        Console.Error.WriteLine(new string('═', 72));

        for (int i = 0; i < messages.Count; i++)
        {
            if (i > 0)
            {
                Console.WriteLine();
                Console.WriteLine(new string('═', 72));
                Console.WriteLine();
            }
            Console.WriteLine($"[{i + 1}/{messages.Count}]");
            Show.RenderMessage(messages[i]!, rawBody);
        }
    }
}
