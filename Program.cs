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
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool reply <id> [--body \"text\"] [--attach path]..."); return 2; }
            await Reply.RunAsync(
                rest[0],
                Args.ParseFlag(rest, "--body") ?? "",
                cmd == "reply-all" || rest.Contains("--all"),
                Args.ParseMultiFlag(rest, "--attach"),
                CancellationToken.None);
            break;
        }

        case "forward":
        {
            if (rest.Length < 1) { Console.Error.WriteLine("Usage: mailtool forward <id> --to addr [--to addr]... [--body \"text\"] [--attach path]..."); return 2; }
            await Forward.RunAsync(
                rest[0],
                Args.ParseMultiFlag(rest, "--to"),
                Args.ParseFlag(rest, "--body") ?? "",
                Args.ParseMultiFlag(rest, "--attach"),
                CancellationToken.None);
            break;
        }

        case "send":
        {
            var sendTo = Args.ParseMultiFlag(rest, "--to");
            if (sendTo.Length == 0) { Console.Error.WriteLine("Usage: mailtool send --to addr [--to addr]... [--cc addr]... --subject \"text\" [--body \"text\"] [--attach path]..."); return 2; }
            await Send.RunAsync(
                sendTo,
                Args.ParseMultiFlag(rest, "--cc"),
                Args.ParseFlag(rest, "--subject") ?? "",
                Args.ParseFlag(rest, "--body") ?? "",
                Args.ParseMultiFlag(rest, "--attach"),
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
          send --to addr [--to addr]... [--cc addr]... --subject "text" [--body "text"] [--attach path]...
          draft [--to addr]... [--cc addr]... [--subject "text"] [--body "text"] [--attach path]...
          reply <id> [--body "text"] [--attach path]...
          reply-all <id> [--body "text"] [--attach path]...
          forward <id> --to addr [--to addr]... [--body "text"] [--attach path]...
          delete <id> [<id>...]

        ORGANIZE
          move <id> [<id>...] --to <folder> [--create] [--dry-run]
          move --to <folder> [--create] [--dry-run]
            Selectors (one or more): --from <sender> --subject-match <regex>
                                     --in-folder <alias|path|id> --since <date> --until <date>
          folders list [--json]                             Folder tree (or JSON array with paths).
          folders create <path>                             Create (supports nesting "Parent/Child").
          folders delete <name-or-id>                       Delete a folder.

        OTHER
          login      Authenticate and verify identity (device-code flow).
          signout    Remove cached auth record.
          help       This text.

        ENV:
          MAILTOOL_CACHE=<path>   Override cache directory.
          MAILTOOL_DEBUG=1        Print stack traces on error.
        """);
}
