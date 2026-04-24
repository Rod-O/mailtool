using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>
/// Mail-folder utilities: list, create, delete, and path-based resolution.
/// Folders are referenced by well-known alias (inbox, sent, drafts, trash, archive, junk),
/// display name (top-level or nested via "Parent/Child"), or raw Graph id.
/// </summary>
public static class Folders
{
    // Graph accepts these strings as folder identifiers directly — no API lookup needed.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inbox"]        = "inbox",
        ["sent"]         = "sentitems",
        ["sentitems"]    = "sentitems",
        ["drafts"]       = "drafts",
        ["deleted"]      = "deleteditems",
        ["deleteditems"] = "deleteditems",
        ["trash"]        = "deleteditems",
        ["archive"]      = "archive",
        ["junk"]         = "junkemail",
        ["junkemail"]    = "junkemail",
        ["outbox"]       = "outbox"
    };

    /// <summary>Prints the full folder tree with item counts. When <paramref name="json"/> is true, emits a flat JSON array with full paths.</summary>
    public static async Task ListAsync(bool json, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var top = await client.Me.MailFolders.GetAsync(cfg =>
        {
            cfg.QueryParameters.Top = 200;
            cfg.QueryParameters.Select = ["id", "displayName", "childFolderCount", "totalItemCount", "unreadItemCount"];
        }, cancellationToken: ct);

        if (top?.Value is null || top.Value.Count == 0)
        {
            if (json) Console.WriteLine("[]");
            else Console.WriteLine("(no folders)");
            return;
        }

        if (json)
        {
            var arr = new System.Text.Json.Nodes.JsonArray();
            foreach (var f in top.Value)
                await CollectJsonAsync(client, f, parentPath: "", arr, ct);
            Console.WriteLine(arr.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        foreach (var f in top.Value)
            await PrintTreeAsync(client, f, 0, ct);
    }

    private static async Task CollectJsonAsync(GraphServiceClient client, MailFolder folder, string parentPath, System.Text.Json.Nodes.JsonArray arr, CancellationToken ct)
    {
        var path = string.IsNullOrEmpty(parentPath) ? (folder.DisplayName ?? "") : $"{parentPath}/{folder.DisplayName}";
        arr.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = folder.Id,
            ["path"] = path,
            ["displayName"] = folder.DisplayName,
            ["totalItemCount"] = folder.TotalItemCount ?? 0,
            ["unreadItemCount"] = folder.UnreadItemCount ?? 0,
            ["childFolderCount"] = folder.ChildFolderCount ?? 0
        });

        if ((folder.ChildFolderCount ?? 0) > 0 && folder.Id is not null)
        {
            var kids = await client.Me.MailFolders[folder.Id].ChildFolders.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 200;
                cfg.QueryParameters.Select = ["id", "displayName", "childFolderCount", "totalItemCount", "unreadItemCount"];
            }, cancellationToken: ct);
            if (kids?.Value is not null)
                foreach (var k in kids.Value)
                    await CollectJsonAsync(client, k, path, arr, ct);
        }
    }

    /// <summary>Ensures the folder path exists, creating any missing segments.</summary>
    public static async Task CreateAsync(string path, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var id = await EnsurePathAsync(client, path, ct);
        Console.Error.WriteLine($"Ready: {path}  (id: {id})");
    }

    /// <summary>Deletes a folder by alias, display name, path, or raw id.</summary>
    public static async Task DeleteAsync(string spec, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var id = await ResolveAsync(client, spec, create: false, ct);
        if (id is null)
        {
            Console.Error.WriteLine($"Folder not found: {spec}");
            Environment.Exit(1);
            return;
        }
        await client.Me.MailFolders[id].DeleteAsync(cancellationToken: ct);
        Console.Error.WriteLine($"Deleted folder: {spec}");
    }

    /// <summary>
    /// Resolves a folder spec to the concrete Graph folder id. Accepts well-known aliases,
    /// display names, nested "Parent/Child" paths, and raw ids. When
    /// <paramref name="create"/> is true, missing path segments are created on the fly.
    /// Aliases are resolved to the real Graph id (required for filter matching against
    /// cached messages' <c>parentFolderId</c>).
    /// </summary>
    public static async Task<string?> ResolveAsync(GraphServiceClient client, string spec, bool create, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;

        var trimmed = spec.Trim();

        // Aliases: look up the real id via Graph so callers can compare against stored parentFolderId.
        if (!trimmed.Contains('/') && Aliases.TryGetValue(trimmed, out var alias))
        {
            try
            {
                var folder = await client.Me.MailFolders[alias].GetAsync(cancellationToken: ct);
                return folder?.Id ?? alias;
            }
            catch
            {
                return alias;
            }
        }

        return create
            ? await EnsurePathAsync(client, trimmed, ct)
            : await FindPathAsync(client, trimmed, ct);
    }

    private static async Task<string> EnsurePathAsync(GraphServiceClient client, string path, CancellationToken ct)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Empty folder path.", nameof(path));

        string? parentId = null;
        foreach (var seg in segments)
        {
            var children = await GetChildrenAsync(client, parentId, ct);
            var existing = children?.FirstOrDefault(f =>
                string.Equals(f.DisplayName, seg, StringComparison.OrdinalIgnoreCase));

            if (existing?.Id is not null)
            {
                parentId = existing.Id;
                continue;
            }

            var body = new MailFolder { DisplayName = seg, IsHidden = false };
            var created = parentId is null
                ? await client.Me.MailFolders.PostAsync(body, cancellationToken: ct)
                : await client.Me.MailFolders[parentId].ChildFolders.PostAsync(body, cancellationToken: ct);

            if (created?.Id is null)
                throw new InvalidOperationException($"Failed to create folder segment: {seg}");

            Console.Error.WriteLine($"  Created folder: {seg}");
            parentId = created.Id;
        }

        return parentId!;
    }

    private static async Task<string?> FindPathAsync(GraphServiceClient client, string path, CancellationToken ct)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        string? parentId = null;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var children = await GetChildrenAsync(client, parentId, ct);
            var hit = children?.FirstOrDefault(f =>
                string.Equals(f.DisplayName, seg, StringComparison.OrdinalIgnoreCase));

            if (hit?.Id is null)
            {
                // If the caller passed a single-segment spec and no display match exists,
                // assume it's a raw Graph id and hand it back to the API to decide.
                if (segments.Length == 1 && parentId is null) return seg;
                return null;
            }
            parentId = hit.Id;
        }
        return parentId;
    }

    private static async Task<List<MailFolder>?> GetChildrenAsync(GraphServiceClient client, string? parentId, CancellationToken ct)
    {
        if (parentId is null)
        {
            var top = await client.Me.MailFolders.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 200;
                cfg.QueryParameters.Select = ["id", "displayName"];
            }, cancellationToken: ct);
            return top?.Value;
        }

        var kids = await client.Me.MailFolders[parentId].ChildFolders.GetAsync(cfg =>
        {
            cfg.QueryParameters.Top = 200;
            cfg.QueryParameters.Select = ["id", "displayName"];
        }, cancellationToken: ct);
        return kids?.Value;
    }

    private static async Task PrintTreeAsync(GraphServiceClient client, MailFolder folder, int depth, CancellationToken ct)
    {
        var indent = new string(' ', depth * 2);
        var total = folder.TotalItemCount ?? 0;
        var unread = folder.UnreadItemCount ?? 0;
        Console.WriteLine($"{indent}- {folder.DisplayName}  [{total} total, {unread} unread]");
        Console.WriteLine($"{indent}  id: {folder.Id}");

        if ((folder.ChildFolderCount ?? 0) > 0 && folder.Id is not null)
        {
            var kids = await client.Me.MailFolders[folder.Id].ChildFolders.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 200;
                cfg.QueryParameters.Select = ["id", "displayName", "childFolderCount", "totalItemCount", "unreadItemCount"];
            }, cancellationToken: ct);

            if (kids?.Value is not null)
                foreach (var k in kids.Value)
                    await PrintTreeAsync(client, k, depth + 1, ct);
        }
    }
}
