using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>Shared utilities used across send/reply/forward commands.</summary>
internal static class Helpers
{
    /// <summary>Resolves a full message id from an exact match or unique prefix.</summary>
    internal static string? ResolveId(string prefix, Index index)
    {
        if (index.ById.ContainsKey(prefix)) return prefix;
        var match = index.ById.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        return match.Count == 1 ? match[0] : null;
    }

    /// <summary>Returns the MIME content type for a file based on its extension.</summary>
    internal static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf"  => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".png"  => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".txt"  => "text/plain",
        ".csv"  => "text/csv",
        _       => "application/octet-stream"
    };

    /// <summary>Attaches local files to a draft message. Exits on first missing file.</summary>
    internal static async Task AttachFilesAsync(GraphServiceClient client, string draftId, string[] paths, CancellationToken ct)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Attachment not found: {path}");
                Environment.Exit(1);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            var attachment = new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = Path.GetFileName(path),
                ContentBytes = bytes,
                ContentType = MimeType(path)
            };

            await client.Me.Messages[draftId].Attachments.PostAsync(attachment, cancellationToken: ct);
            Console.Error.WriteLine($"  Attached: {Path.GetFileName(path)} ({bytes.Length / 1024}KB)");
        }
    }
}
