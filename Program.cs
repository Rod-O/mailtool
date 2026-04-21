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

        case "backfill":
            await Backfill.RunAsync(Args.ParseFolders(rest), Args.ParsePages(rest), CancellationToken.None);
            break;

        case "search":
            Search.Run(Args.ParseSearchOptions(rest));
            break;

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
          sync [--folder inbox|sent|all]               Incremental delta sync. Default: inbox + sent.
          backfill [--folder ...] [--pages N]          Page backward from oldest cached message (default: 2 pages).
          search [query] [options]                     Search local cache.
            --from <substr>   --to <substr>   --subject <substr>
            --since <date>    --until <date>  --limit <n>   --body
          show <id> [--raw]                            Print a single message (HTML→text).
          thread <conv-id | msg-id> [--raw]            Reconstruct a conversation.
          status                                       Cache stats + last sync per folder.

        COMPOSE
          send --to addr [--to addr]... [--cc addr]... --subject "text" [--body "text"] [--attach path]...
          draft [--to addr]... [--cc addr]... [--subject "text"] [--body "text"] [--attach path]...
          reply <id> [--body "text"] [--attach path]...
          reply-all <id> [--body "text"] [--attach path]...
          forward <id> --to addr [--to addr]... [--body "text"] [--attach path]...
          delete <id> [<id>...]

        OTHER
          login      Authenticate and verify identity (device-code flow).
          signout    Remove cached auth record.
          help       This text.

        ENV:
          MAILTOOL_CACHE=<path>   Override cache directory (default: ~/.local/share/mailtool/cache).
          MAILTOOL_DEBUG=1        Print stack traces on error.
        """);
}
