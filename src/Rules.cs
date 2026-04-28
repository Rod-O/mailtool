using System.Text.Json.Nodes;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>
/// Server-side Exchange/Outlook mailbox rules ("Inbox rules"). These run on every
/// incoming message even when mailtool is not running. Stored on the mail folder
/// (inbox by default). Conditions and actions map to Graph's MessageRule schema.
/// </summary>
public static class Rules
{
    /// <summary>Lists rules on the target folder (inbox by default).</summary>
    public static async Task ListAsync(string folderSpec, bool json, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var folderId = await Folders.ResolveAsync(client, folderSpec, create: false, ct)
                       ?? throw new InvalidOperationException($"Folder not found: {folderSpec}");

        var rules = await client.Me.MailFolders[folderId].MessageRules.GetAsync(cancellationToken: ct);
        var items = rules?.Value ?? [];

        if (json)
        {
            var arr = new JsonArray();
            foreach (var r in items.OrderBy(r => r.Sequence ?? int.MaxValue))
                arr.Add(RuleToJson(r));
            Console.WriteLine(arr.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (items.Count == 0)
        {
            Console.WriteLine("(no rules)");
            return;
        }

        foreach (var r in items.OrderBy(r => r.Sequence ?? int.MaxValue))
        {
            var enabled = (r.IsEnabled ?? false) ? "on " : "off";
            var seq = r.Sequence?.ToString() ?? "?";
            Console.WriteLine($"  [{enabled}] #{seq,-3} {r.DisplayName}");
            Console.WriteLine($"         id: {r.Id}");
            var cond = SummarizeConditions(r.Conditions);
            var act = SummarizeActions(r.Actions);
            if (!string.IsNullOrEmpty(cond)) Console.WriteLine($"         when: {cond}");
            if (!string.IsNullOrEmpty(act))  Console.WriteLine($"         do:   {act}");
        }
    }

    /// <summary>Prints a single rule's full conditions and actions.</summary>
    public static async Task ShowAsync(string folderSpec, string spec, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var folderId = await Folders.ResolveAsync(client, folderSpec, create: false, ct)
                       ?? throw new InvalidOperationException($"Folder not found: {folderSpec}");

        var rule = await ResolveRuleAsync(client, folderId, spec, ct);
        Console.WriteLine(RuleToJson(rule).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Creates a new rule. At least one condition and one action are required.</summary>
    public static async Task CreateAsync(
        string folderSpec,
        string name,
        string[] fromAddresses,
        string[] sentToAddresses,
        string[] subjectContains,
        string[] bodyContains,
        bool hasAttachment,
        string? toFolderSpec,
        bool markRead,
        bool delete,
        string[] forwardTo,
        bool stopProcessing,
        int? sequence,
        bool disabled,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("--name is required.");

        var conditions = BuildConditions(fromAddresses, sentToAddresses, subjectContains, bodyContains, hasAttachment);
        if (conditions is null)
            throw new ArgumentException("At least one condition is required (--from / --sent-to / --subject-contains / --body-contains / --has-attachment).");

        var client = await Auth.GetClientAsync(ct);
        var folderId = await Folders.ResolveAsync(client, folderSpec, create: false, ct)
                       ?? throw new InvalidOperationException($"Folder not found: {folderSpec}");

        var actions = await BuildActionsAsync(client, toFolderSpec, markRead, delete, forwardTo, stopProcessing, ct);
        if (actions is null)
            throw new ArgumentException("At least one action is required (--to-folder / --mark-read / --delete / --forward-to / --stop).");

        var body = new MessageRule
        {
            DisplayName = name,
            IsEnabled = !disabled,
            Sequence = sequence,
            Conditions = conditions,
            Actions = actions
        };

        var created = await client.Me.MailFolders[folderId].MessageRules.PostAsync(body, cancellationToken: ct);
        Console.Error.WriteLine($"Created rule: {created?.DisplayName} (id: {created?.Id})");
    }

    /// <summary>Deletes a rule by id or display name.</summary>
    public static async Task DeleteAsync(string folderSpec, string spec, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var folderId = await Folders.ResolveAsync(client, folderSpec, create: false, ct)
                       ?? throw new InvalidOperationException($"Folder not found: {folderSpec}");
        var rule = await ResolveRuleAsync(client, folderId, spec, ct);
        await client.Me.MailFolders[folderId].MessageRules[rule.Id].DeleteAsync(cancellationToken: ct);
        Console.Error.WriteLine($"Deleted rule: {rule.DisplayName}");
    }

    /// <summary>Toggles a rule's enabled flag.</summary>
    public static async Task SetEnabledAsync(string folderSpec, string spec, bool enabled, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var folderId = await Folders.ResolveAsync(client, folderSpec, create: false, ct)
                       ?? throw new InvalidOperationException($"Folder not found: {folderSpec}");
        var rule = await ResolveRuleAsync(client, folderId, spec, ct);
        var patch = new MessageRule { IsEnabled = enabled };
        await client.Me.MailFolders[folderId].MessageRules[rule.Id].PatchAsync(patch, cancellationToken: ct);
        Console.Error.WriteLine($"{(enabled ? "Enabled" : "Disabled")} rule: {rule.DisplayName}");
    }

    // --- helpers -----------------------------------------------------------

    private static async Task<MessageRule> ResolveRuleAsync(GraphServiceClient client, string folderId, string spec, CancellationToken ct)
    {
        var all = await client.Me.MailFolders[folderId].MessageRules.GetAsync(cancellationToken: ct);
        var items = all?.Value ?? [];
        var byId = items.FirstOrDefault(r => string.Equals(r.Id, spec, StringComparison.Ordinal));
        if (byId is not null) return byId;
        var byName = items.Where(r => string.Equals(r.DisplayName, spec, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1) return byName[0];
        if (byName.Count > 1) throw new InvalidOperationException($"Multiple rules named '{spec}'. Use the id instead.");
        throw new InvalidOperationException($"Rule not found: {spec}");
    }

    private static MessageRulePredicates? BuildConditions(
        string[] fromAddresses, string[] sentToAddresses, string[] subjectContains, string[] bodyContains, bool hasAttachment)
    {
        var any = fromAddresses.Length > 0 || sentToAddresses.Length > 0
               || subjectContains.Length > 0 || bodyContains.Length > 0
               || hasAttachment;
        if (!any) return null;

        var p = new MessageRulePredicates();
        if (fromAddresses.Length > 0)
            p.FromAddresses = fromAddresses.Select(ToRecipient).ToList();
        if (sentToAddresses.Length > 0)
            p.SentToAddresses = sentToAddresses.Select(ToRecipient).ToList();
        if (subjectContains.Length > 0)
            p.SubjectContains = subjectContains.ToList();
        if (bodyContains.Length > 0)
            p.BodyContains = bodyContains.ToList();
        if (hasAttachment)
            p.HasAttachments = true;
        return p;
    }

    private static async Task<MessageRuleActions?> BuildActionsAsync(
        GraphServiceClient client, string? toFolderSpec, bool markRead, bool delete, string[] forwardTo, bool stopProcessing, CancellationToken ct)
    {
        var any = !string.IsNullOrEmpty(toFolderSpec) || markRead || delete || forwardTo.Length > 0 || stopProcessing;
        if (!any) return null;

        var a = new MessageRuleActions();
        if (!string.IsNullOrEmpty(toFolderSpec))
        {
            var destId = await Folders.ResolveAsync(client, toFolderSpec, create: false, ct)
                         ?? throw new InvalidOperationException($"Destination folder not found: {toFolderSpec}");
            a.MoveToFolder = destId;
        }
        if (markRead) a.MarkAsRead = true;
        if (delete) a.Delete = true;
        if (forwardTo.Length > 0)
            a.ForwardTo = forwardTo.Select(ToRecipient).ToList();
        if (stopProcessing) a.StopProcessingRules = true;
        return a;
    }

    private static Recipient ToRecipient(string addr) => new()
    {
        EmailAddress = new EmailAddress { Address = addr.Trim() }
    };

    private static string SummarizeConditions(MessageRulePredicates? p)
    {
        if (p is null) return "";
        var parts = new List<string>();
        if (p.FromAddresses?.Count > 0)
            parts.Add($"from={string.Join(",", p.FromAddresses.Select(r => r.EmailAddress?.Address ?? "?"))}");
        if (p.SentToAddresses?.Count > 0)
            parts.Add($"to={string.Join(",", p.SentToAddresses.Select(r => r.EmailAddress?.Address ?? "?"))}");
        if (p.SubjectContains?.Count > 0)
            parts.Add($"subject~{string.Join("|", p.SubjectContains)}");
        if (p.BodyContains?.Count > 0)
            parts.Add($"body~{string.Join("|", p.BodyContains)}");
        if (p.HasAttachments == true) parts.Add("has-attachment");
        return string.Join(" & ", parts);
    }

    private static string SummarizeActions(MessageRuleActions? a)
    {
        if (a is null) return "";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(a.MoveToFolder)) parts.Add($"move→{a.MoveToFolder![..Math.Min(20, a.MoveToFolder!.Length)]}…");
        if (a.MarkAsRead == true) parts.Add("mark-read");
        if (a.Delete == true) parts.Add("delete");
        if (a.ForwardTo?.Count > 0) parts.Add($"forward→{string.Join(",", a.ForwardTo.Select(r => r.EmailAddress?.Address ?? "?"))}");
        if (a.StopProcessingRules == true) parts.Add("stop");
        return string.Join(" + ", parts);
    }

    private static JsonObject RuleToJson(MessageRule r)
    {
        var obj = new JsonObject
        {
            ["id"] = r.Id,
            ["displayName"] = r.DisplayName,
            ["sequence"] = r.Sequence,
            ["isEnabled"] = r.IsEnabled,
            ["conditions"] = ConditionsToJson(r.Conditions),
            ["actions"] = ActionsToJson(r.Actions)
        };
        return obj;
    }

    private static JsonObject ConditionsToJson(MessageRulePredicates? p)
    {
        var o = new JsonObject();
        if (p is null) return o;
        if (p.FromAddresses?.Count > 0)
            o["fromAddresses"] = new JsonArray(p.FromAddresses.Select(r => (JsonNode?)r.EmailAddress?.Address).ToArray());
        if (p.SentToAddresses?.Count > 0)
            o["sentToAddresses"] = new JsonArray(p.SentToAddresses.Select(r => (JsonNode?)r.EmailAddress?.Address).ToArray());
        if (p.SubjectContains?.Count > 0)
            o["subjectContains"] = new JsonArray(p.SubjectContains.Select(s => (JsonNode?)s).ToArray());
        if (p.BodyContains?.Count > 0)
            o["bodyContains"] = new JsonArray(p.BodyContains.Select(s => (JsonNode?)s).ToArray());
        if (p.HasAttachments is not null) o["hasAttachments"] = p.HasAttachments;
        return o;
    }

    private static JsonObject ActionsToJson(MessageRuleActions? a)
    {
        var o = new JsonObject();
        if (a is null) return o;
        if (!string.IsNullOrEmpty(a.MoveToFolder)) o["moveToFolder"] = a.MoveToFolder;
        if (a.MarkAsRead is not null) o["markAsRead"] = a.MarkAsRead;
        if (a.Delete is not null) o["delete"] = a.Delete;
        if (a.ForwardTo?.Count > 0)
            o["forwardTo"] = new JsonArray(a.ForwardTo.Select(r => (JsonNode?)r.EmailAddress?.Address).ToArray());
        if (a.StopProcessingRules is not null) o["stopProcessingRules"] = a.StopProcessingRules;
        return o;
    }
}
