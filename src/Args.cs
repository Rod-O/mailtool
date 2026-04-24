namespace MailTool;

/// <summary>Command-line argument parsing helpers.</summary>
public static class Args
{
    /// <summary>Returns the value immediately following <paramref name="flag"/>, or null if not found.</summary>
    public static string? ParseFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) return args[i + 1];
        return null;
    }

    /// <summary>Returns all values following each occurrence of <paramref name="flag"/>.</summary>
    public static string[] ParseMultiFlag(string[] args, string flag)
    {
        var result = new List<string>();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag) result.Add(args[i + 1]);
        return result.ToArray();
    }

    /// <summary>Returns true if <paramref name="flag"/> appears anywhere in <paramref name="args"/>.</summary>
    public static bool HasFlag(string[] args, string flag) => args.Contains(flag);

    /// <summary>Returns the value of <c>--pages</c>, defaulting to 2.</summary>
    public static int ParsePages(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--pages" && int.TryParse(args[i + 1], out var n)) return n;
        return 2;
    }

    /// <summary>
    /// Returns the folder list from <c>--folder</c>. Accepts <c>inbox</c>, <c>sent</c>, <c>all</c>,
    /// or a raw folder id. Defaults to inbox + sentitems.
    /// </summary>
    public static string[] ParseFolders(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--folder" && i + 1 < args.Length)
            {
                return args[i + 1].ToLowerInvariant() switch
                {
                    "inbox" => ["inbox"],
                    "sent"  => ["sentitems"],
                    "all"   => ["inbox", "sentitems"],
                    var v   => [v]
                };
            }
        }
        return ["inbox", "sentitems"];
    }

    /// <summary>Parses the full set of search filter flags into a <see cref="SearchOptions"/> instance.</summary>
    public static SearchOptions ParseSearchOptions(string[] args)
    {
        var opts = new SearchOptions();
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from"          when i + 1 < args.Length: opts.From         = args[++i]; break;
                case "--to"            when i + 1 < args.Length: opts.To           = args[++i]; break;
                case "--subject"       when i + 1 < args.Length: opts.Subject      = args[++i]; break;
                case "--subject-match" when i + 1 < args.Length: opts.SubjectRegex = args[++i]; break;
                case "--since"         when i + 1 < args.Length: opts.Since        = DateTimeOffset.Parse(args[++i]); break;
                case "--until"         when i + 1 < args.Length: opts.Until        = DateTimeOffset.Parse(args[++i]); break;
                case "--limit"         when i + 1 < args.Length: opts.Limit        = int.Parse(args[++i]); break;
                case "--in-folder"     when i + 1 < args.Length: opts.InFolder     = args[++i]; break;
                case "--body": opts.BodyMatch = true; break;
                case "--json": opts.Json = true; break;
                default: positional.Add(args[i]); break;
            }
        }

        if (positional.Count > 0)
            opts.Query = string.Join(" ", positional);

        return opts;
    }
}
