using MailTool;

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

var cmd  = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    switch (cmd)
    {
        case "sync":
            await Sync.RunAsync(Args.ParseFolders(rest), CancellationToken.None);
            break;

        case "reply":
        case "reply-all":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool reply <id> [--body \"text\"] [--attach path]... [--yes]"); return 2; }
            await Reply.RunAsync(
                rest[0],
                Args.ParseFlag(rest, "--body") ?? "",
                cmd == "reply-all" || rest.Contains("--all"),
                Args.ParseMultiFlag(rest, "--attach"),
                rest.Contains("--yes") || rest.Contains("-y"),
                CancellationToken.None);
            break;
        }

        case "forward":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool forward <id> --to addr [--to addr]... [--body \"text\"] [--attach path]... [--yes]"); return 2; }
            await Forward.RunAsync(
                rest[0],
                Args.ParseMultiFlag(rest, "--to"),
                Args.ParseFlag(rest, "--body") ?? "",
                Args.ParseMultiFlag(rest, "--attach"),
                rest.Contains("--yes") || rest.Contains("-y"),
                CancellationToken.None);
            break;
        }

        case "send":
        {
            var sendTo = Args.ParseMultiFlag(rest, "--to");
            if (sendTo.Length == 0) { Console.Error.WriteLine("Usage: mailtool send --to addr [--to addr]... [--cc addr]... --subject \"text\" [--body \"text\"] [--attach path]... [--yes]"); return 2; }
            await Send.RunAsync(
                sendTo,
                Args.ParseMultiFlag(rest, "--cc"),
                Args.ParseFlag(rest, "--subject") ?? "",
                Args.ParseFlag(rest, "--body") ?? "",
                Args.ParseMultiFlag(rest, "--attach"),
                rest.Contains("--yes") || rest.Contains("-y"),
                CancellationToken.None);
            break;
        }

        case "draft":
        {
            await Draft.RunAsync(
                Args.ParseMultiFlag(rest, "--to"),
                Args.ParseMultiFlag(rest, "--cc"),
                Args.ParseFlag(rest, "--subject") ?? "",
                Args.ParseFlag(rest, "--body") ?? "",
                Args.ParseMultiFlag(rest, "--attach"),
                CancellationToken.None);
            break;
        }

        case "delete":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool delete <id> [<id>...]"); return 2; }
            await Delete.RunAsync(rest.Where(a => !a.StartsWith('-')).ToArray(), CancellationToken.None);
            break;
        }

        case "move":
        {
            var dest = Args.ParseFlag(rest, "--to");
            if (dest is null) { Console.Error.WriteLine("Usage: mailtool move <id>... --to <folder> [--create] [--dry-run]  |  mailtool move --from <sender> --to <folder> [--in-folder <src>] [--since D] [--until D] [--subject-match <regex>] [--dry-run] [--create]"); return 2; }

            // Strip the --to value pair before reusing Args.ParseSearchOptions (where --to means recipient).
            var filterArgs = new List<string>();
            var ids = new List<string>();
            for (int i = 0; i < rest.Length; i++)
            {
                if (rest[i] == "--to") { i++; continue; }  // move's --to is the destination, not a search filter
                if (rest[i] == "--create" || rest[i] == "--dry-run") continue;
                if (rest[i].StartsWith('-') || IsFilterValue(rest, i))
                {
                    filterArgs.Add(rest[i]);
                }
                else
                {
                    ids.Add(rest[i]);
                }
            }

            var selector = Args.ParseSearchOptions(filterArgs.ToArray());
            selector.Query = null; // move never treats free-text as a filter

            await Move.RunAsync(
                ids.ToArray(),
                selector,
                dest,
                createIfMissing: rest.Contains("--create"),
                dryRun: rest.Contains("--dry-run"),
                CancellationToken.None);
            break;

            static bool IsFilterValue(string[] args, int i)
            {
                if (i == 0) return false;
                var prev = args[i - 1];
                return prev is "--from" or "--subject" or "--subject-match"
                    or "--since" or "--until" or "--in-folder" or "--limit";
            }
        }

        case "folders":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool folders list [--json] | create <path> | delete <name-or-id>"); return 2; }
            var sub = rest[0].ToLowerInvariant();
            switch (sub)
            {
                case "list":
                    await Folders.ListAsync(json: rest.Contains("--json"), CancellationToken.None);
                    break;
                case "create":
                    if (rest.Length < 2) { Console.Error.WriteLine("Usage: mailtool folders create <path>"); return 2; }
                    await Folders.CreateAsync(rest[1], CancellationToken.None);
                    break;
                case "delete":
                    if (rest.Length < 2) { Console.Error.WriteLine("Usage: mailtool folders delete <name-or-id>"); return 2; }
                    await Folders.DeleteAsync(rest[1], CancellationToken.None);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown folders subcommand: {sub}");
                    return 2;
            }
            break;
        }

        case "backfill":
            await Backfill.RunAsync(Args.ParseFolders(rest), Args.ParsePages(rest), CancellationToken.None);
            break;

        case "calendar":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool calendar create|list|show|delete [...]"); return 2; }
            var sub = rest[0].ToLowerInvariant();
            switch (sub)
            {
                case "create":
                {
                    var subject  = Args.ParseFlag(rest, "--subject");
                    var startStr = Args.ParseFlag(rest, "--start");
                    var endStr   = Args.ParseFlag(rest, "--end");
                    if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
                    {
                        Console.Error.WriteLine("Usage: mailtool calendar create --subject \"<text>\" --start \"<datetime>\" --end \"<datetime>\" [--timezone <tz>] [--attendees <addr>]... [--optional <addr>]... [--body \"<text>\"] [--location \"<text>\"] [--online]");
                        return 2;
                    }
                    await Calendar.CreateAsync(
                        subject,
                        startStr,
                        endStr,
                        Args.ParseFlag(rest, "--timezone") ?? "UTC",
                        Args.ParseMultiFlag(rest, "--attendees"),
                        Args.ParseMultiFlag(rest, "--optional"),
                        Args.ParseFlag(rest, "--body"),
                        Args.ParseFlag(rest, "--location"),
                        rest.Contains("--online"),
                        rest.Contains("--yes") || rest.Contains("-y"),
                        CancellationToken.None);
                    break;
                }
                case "list":
                {
                    int daysAhead = 7, daysBack = 0;
                    var d = Args.ParseFlag(rest, "--days");
                    if (!string.IsNullOrEmpty(d) && int.TryParse(d, out var n)) daysAhead = n;
                    var b = Args.ParseFlag(rest, "--days-back");
                    if (!string.IsNullOrEmpty(b) && int.TryParse(b, out var bn)) daysBack = bn;
                    var view = Args.ParseFlag(rest, "--view") ?? "agenda";
                    var date = Args.ParseFlag(rest, "--date");
                    await Calendar.ListAsync(
                        daysBack, daysAhead,
                        rest.Contains("--json"),
                        live: rest.Contains("--live"),
                        viewMode: view,
                        dateStr: date,
                        CancellationToken.None);
                    break;
                }
                case "sync":
                {
                    int daysBack = 7, daysAhead = 60;
                    var b = Args.ParseFlag(rest, "--days-back");
                    if (!string.IsNullOrEmpty(b) && int.TryParse(b, out var bn)) daysBack = bn;
                    var d = Args.ParseFlag(rest, "--days");
                    if (!string.IsNullOrEmpty(d) && int.TryParse(d, out var dn)) daysAhead = dn;
                    await Calendar.SyncAsync(daysBack, daysAhead, CancellationToken.None);
                    break;
                }
                case "show":
                {
                    var id = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (id is null) { Console.Error.WriteLine("Usage: mailtool calendar show <id>"); return 2; }
                    await Calendar.ShowAsync(id, CancellationToken.None);
                    break;
                }
                case "delete":
                {
                    var id = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (id is null) { Console.Error.WriteLine("Usage: mailtool calendar delete <id>"); return 2; }
                    await Calendar.DeleteAsync(id, CancellationToken.None);
                    break;
                }
                case "update":
                {
                    var id = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (id is null) { Console.Error.WriteLine("Usage: mailtool calendar update <id> [--subject ...] [--start ...] [--end ...] [--timezone ...] [--attendees <a>]... [--add-attendees <a>]... [--add-optional <a>]... [--body ...] [--location ...] [--online | --no-online]"); return 2; }
                    bool? online = null;
                    if (rest.Contains("--online")) online = true;
                    else if (rest.Contains("--no-online")) online = false;
                    await Calendar.UpdateAsync(
                        id,
                        Args.ParseFlag(rest, "--subject"),
                        Args.ParseFlag(rest, "--start"),
                        Args.ParseFlag(rest, "--end"),
                        Args.ParseFlag(rest, "--timezone"),
                        Args.ParseMultiFlag(rest, "--attendees"),
                        Args.ParseMultiFlag(rest, "--add-attendees"),
                        Args.ParseMultiFlag(rest, "--add-optional"),
                        Args.ParseFlag(rest, "--body"),
                        Args.ParseFlag(rest, "--location"),
                        online,
                        CancellationToken.None);
                    break;
                }
                case "respond":
                {
                    var id = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (id is null) { Console.Error.WriteLine("Usage: mailtool calendar respond <id> --accept|--decline|--tentative [--message \"text\"] [--no-send]"); return 2; }
                    string? action = rest.Contains("--accept") ? "accept"
                                   : rest.Contains("--decline") ? "decline"
                                   : rest.Contains("--tentative") ? "tentative"
                                   : null;
                    if (action is null) { Console.Error.WriteLine("Pass one of --accept / --decline / --tentative."); return 2; }
                    await Calendar.RespondAsync(
                        id,
                        action,
                        Args.ParseFlag(rest, "--message"),
                        sendResponse: !rest.Contains("--no-send"),
                        CancellationToken.None);
                    break;
                }
                case "availability":
                {
                    var startStr = Args.ParseFlag(rest, "--start");
                    var endStr   = Args.ParseFlag(rest, "--end");
                    if (string.IsNullOrEmpty(startStr) || string.IsNullOrEmpty(endStr))
                    {
                        Console.Error.WriteLine("Usage: mailtool calendar availability --start <datetime> --end <datetime> [--timezone <tz>] [--interval <minutes>] [--attendees <addr>]...");
                        return 2;
                    }
                    int interval = 30;
                    var iv = Args.ParseFlag(rest, "--interval");
                    if (!string.IsNullOrEmpty(iv) && int.TryParse(iv, out var n)) interval = n;
                    var schedules = Args.ParseMultiFlag(rest, "--attendees");
                    if (schedules.Length == 0) schedules = Args.ParseMultiFlag(rest, "--schedule");
                    await Calendar.AvailabilityAsync(
                        startStr, endStr,
                        Args.ParseFlag(rest, "--timezone") ?? "UTC",
                        schedules,
                        interval,
                        CancellationToken.None);
                    break;
                }
                default:
                    Console.Error.WriteLine($"Unknown calendar subcommand: {sub}");
                    return 2;
            }
            break;
        }

        case "rules":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool rules list|show|create|delete|enable|disable [...]"); return 2; }
            var sub = rest[0].ToLowerInvariant();
            var folder = Args.ParseFlag(rest, "--in-folder") ?? "inbox";
            switch (sub)
            {
                case "list":
                    await Rules.ListAsync(folder, json: rest.Contains("--json"), CancellationToken.None);
                    break;
                case "show":
                {
                    var spec = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (spec is null) { Console.Error.WriteLine("Usage: mailtool rules show <id-or-name> [--in-folder <folder>]"); return 2; }
                    await Rules.ShowAsync(folder, spec, CancellationToken.None);
                    break;
                }
                case "create":
                {
                    var name = Args.ParseFlag(rest, "--name");
                    if (string.IsNullOrWhiteSpace(name)) { Console.Error.WriteLine("Usage: mailtool rules create --name \"<name>\" [conditions] [actions]"); return 2; }
                    int? sequence = null;
                    var seqStr = Args.ParseFlag(rest, "--sequence");
                    if (!string.IsNullOrEmpty(seqStr) && int.TryParse(seqStr, out var s)) sequence = s;
                    await Rules.CreateAsync(
                        folder,
                        name,
                        Args.ParseMultiFlag(rest, "--from"),
                        Args.ParseMultiFlag(rest, "--sent-to"),
                        Args.ParseMultiFlag(rest, "--subject-contains"),
                        Args.ParseMultiFlag(rest, "--body-contains"),
                        rest.Contains("--has-attachment"),
                        Args.ParseFlag(rest, "--to-folder"),
                        rest.Contains("--mark-read"),
                        rest.Contains("--delete"),
                        Args.ParseMultiFlag(rest, "--forward-to"),
                        rest.Contains("--stop"),
                        sequence,
                        rest.Contains("--disabled"),
                        CancellationToken.None);
                    break;
                }
                case "delete":
                {
                    var spec = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (spec is null) { Console.Error.WriteLine("Usage: mailtool rules delete <id-or-name> [--in-folder <folder>]"); return 2; }
                    await Rules.DeleteAsync(folder, spec, CancellationToken.None);
                    break;
                }
                case "enable":
                case "disable":
                {
                    var spec = rest.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
                    if (spec is null) { Console.Error.WriteLine($"Usage: mailtool rules {sub} <id-or-name> [--in-folder <folder>]"); return 2; }
                    await Rules.SetEnabledAsync(folder, spec, enabled: sub == "enable", CancellationToken.None);
                    break;
                }
                default:
                    Console.Error.WriteLine($"Unknown rules subcommand: {sub}");
                    return 2;
            }
            break;
        }

        case "search":
        {
            var opts = Args.ParseSearchOptions(rest);
            if (!string.IsNullOrEmpty(opts.InFolder))
            {
                var client = await Auth.GetClientAsync(CancellationToken.None);
                opts.InFolderId = await Folders.ResolveAsync(client, opts.InFolder, create: false, CancellationToken.None);
                if (opts.InFolderId is null) { Console.Error.WriteLine($"Folder not found: {opts.InFolder}"); return 1; }
            }
            Search.Run(opts);
            break;
        }

        case "stats":
        {
            var opts = Args.ParseSearchOptions(rest);
            opts.Query = null; // stats ignores free-text query
            if (!string.IsNullOrEmpty(opts.InFolder))
            {
                var client = await Auth.GetClientAsync(CancellationToken.None);
                opts.InFolderId = await Folders.ResolveAsync(client, opts.InFolder, create: false, CancellationToken.None);
                if (opts.InFolderId is null) { Console.Error.WriteLine($"Folder not found: {opts.InFolder}"); return 1; }
            }
            Stats.Run(opts);
            break;
        }

        case "show":
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool show <id> [--raw]"); return 2; }
            Show.Run(rest[0], rest.Contains("--raw"));
            break;

        case "thread":
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool thread <conversation-id | message-id> [--raw]"); return 2; }
            ThreadCmd.Run(rest[0], rest.Contains("--raw"));
            break;

        case "login":
        {
            var client = await Auth.GetClientAsync(CancellationToken.None);
            var me = await client.Me.GetAsync(cancellationToken: CancellationToken.None);
            Console.Error.WriteLine($"Authenticated as: {me?.DisplayName} <{me?.Mail}>");
            break;
        }

        case "signout":
            Auth.SignOut();
            break;

        case "status":
            PrintStatus();
            break;

        case "help":
        case "--help":
        case "-h":
            PrintHelp();
            break;

        default:
            Console.Error.WriteLine($"Unknown command: {cmd}");
            PrintHelp();
            return 2;
    }
    return Environment.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    if (Environment.GetEnvironmentVariable("MAILTOOL_DEBUG") == "1")
        Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static void PrintStatus()
{
    var state = Storage.LoadState();
    var index = Storage.LoadIndex();
    Console.WriteLine($"Cache: {Storage.CacheRoot}");
    Console.WriteLine($"Messages indexed: {index.ById.Count}");
    Console.WriteLine($"Threads indexed:  {index.ByConversation.Count}");
    Console.WriteLine();
    Console.WriteLine("Folders:");
    foreach (var (name, fs) in state.Folders)
    {
        var last = fs.LastSync?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never";
        var hasDelta = !string.IsNullOrEmpty(fs.DeltaLink) ? "yes" : "no";
        Console.WriteLine($"  {name,-12} last-sync: {last}   delta-token: {hasDelta}");
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
        mailtool — local Microsoft 365 mail cache

        USAGE:
          mailtool <command> [options]

        READ
          sync [--folder inbox|sent|all]                    Incremental delta sync.
          backfill [--folder ...] [--pages N]               Page backward from oldest cached message.
          search [query] [opts]                             Search local cache.
            Filters: --from <substr> --to <substr> --subject <substr> --subject-match <regex>
                     --since <date> --until <date> --in-folder <alias|path|id>
                     --limit <n> --body (match inside body) --json
          stats [opts]                                      Aggregate top senders / domains / by-month.
            Same filter flags as search. Supports --json.
          show <id> [--raw]                                 Print a single message.
          thread <conv-id | msg-id> [--raw]                 Reconstruct a conversation.
          status                                            Cache stats + last sync per folder.

        COMPOSE
          send --to addr [--to addr]... [--cc addr]... --subject "text" [--body "text"] [--attach path]... [--yes]
          draft [--to addr]... [--cc addr]... [--subject "text"] [--body "text"] [--attach path]...
          reply <id> [--body "text"] [--attach path]... [--yes]
          reply-all <id> [--body "text"] [--attach path]... [--yes]
          forward <id> --to addr [--to addr]... [--body "text"] [--attach path]... [--yes]
          delete <id> [<id>...]

          All outbound commands (send/reply/reply-all/forward) print a preview and
          prompt [Y]es / [N]o / [R]ead more before dispatch. With stdin redirected
          (non-TTY) they refuse unless --yes is passed.

        ORGANIZE
          move <id> [<id>...] --to <folder> [--create] [--dry-run]
          move --to <folder> [--create] [--dry-run]
            Selectors (one or more): --from <sender> --subject-match <regex>
                                     --in-folder <alias|path|id> --since <date> --until <date>
          folders list [--json]                             Folder tree (or JSON array with paths).
          folders create <path>                             Create (supports nesting "Parent/Child").
          folders delete <name-or-id>                       Delete a folder.

        CALENDAR (M365 events — invites, online meetings, listings)
          calendar create --subject "<text>" --start "<datetime>" --end "<datetime>"
                          [--timezone <tz>] [--attendees <addr>]... [--optional <addr>]...
                          [--body "<text>"] [--location "<text>"] [--online]
          calendar sync   [--days <n>] [--days-back <n>]   Cache events locally for fast offline list/views.
          calendar list   [--days <n>] [--days-back <n>] [--json] [--live]
                          [--view agenda|day|week] [--date YYYY-MM-DD]
          calendar show   <id>
          calendar delete <id>
          calendar update <id> [--subject ...] [--start ...] [--end ...] [--timezone ...]
                          [--attendees <a>]... [--add-attendees <a>]... [--add-optional <a>]...
                          [--body ...] [--location ...] [--online | --no-online]
          calendar respond <id> --accept|--decline|--tentative [--message "text"] [--no-send]
          calendar availability --start <datetime> --end <datetime>
                          [--timezone <tz>] [--interval <minutes>]
                          [--attendees <addr>]...

        RULES (server-side Exchange/Outlook inbox rules — fire on every incoming message)
          rules list [--json] [--in-folder <folder>]        List rules on the folder (default: inbox).
          rules show <id-or-name> [--in-folder <folder>]    Show a rule's full conditions/actions.
          rules create --name "<name>"
                       [--from <addr>]... [--sent-to <addr>]...
                       [--subject-contains <text>]... [--body-contains <text>]...
                       [--has-attachment]
                       [--to-folder <folder>] [--mark-read] [--delete]
                       [--forward-to <addr>]... [--stop]
                       [--sequence <n>] [--disabled]
                       [--in-folder <folder>]
          rules enable  <id-or-name> [--in-folder <folder>]
          rules disable <id-or-name> [--in-folder <folder>]
          rules delete  <id-or-name> [--in-folder <folder>]

        OTHER
          login      Authenticate and verify identity (device-code flow).
          signout    Remove cached auth record.
          help       This text.

        ENV:
          MAILTOOL_CACHE=<path>   Override cache directory.
          MAILTOOL_DEBUG=1        Print stack traces on error.
        """);
}
