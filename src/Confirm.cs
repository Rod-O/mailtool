namespace MailTool;

/// <summary>
/// Interactive confirmation prompt shown before any outbound mail action
/// (send, reply, reply-all, forward) and before calendar event creation
/// with attendees. Built as a hard safety net at the tool layer so any
/// caller — terminal user, AI agent, or script — must explicitly confirm
/// before the message hits the network.
/// </summary>
/// <remarks>
/// Behaviour:
/// <list type="bullet">
///   <item><description>If <c>--yes</c> was passed, returns <see cref="Outcome.Send"/> immediately.</description></item>
///   <item><description>If stdin is a TTY, prints a preview and prompts Yes / No / Read more.</description></item>
///   <item><description>If stdin is redirected (e.g. invoked from a non-interactive harness), prints the preview and refuses — the caller must rerun with <c>--yes</c> or in a terminal.</description></item>
/// </list>
/// </remarks>
public static class Confirm
{
    /// <summary>Outcome of the confirmation prompt.</summary>
    public enum Outcome
    {
        /// <summary>Caller confirmed; proceed with the network call.</summary>
        Send,
        /// <summary>Caller declined or the prompt could not be shown; abort.</summary>
        Cancel
    }

    /// <summary>Confirms an outbound email composition.</summary>
    public static Outcome Email(
        string verb,
        string[] to,
        string[] cc,
        string subject,
        string body,
        bool autoYes,
        int previewLines = 20)
    {
        if (autoYes) return Outcome.Send;

        PrintEmailHeader(verb, to, cc, subject);
        PrintBodyPreview(body, previewLines);

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("✗ Refusing to send: stdin is not a terminal.");
            Console.Error.WriteLine("  Re-run interactively, or pass --yes to bypass the prompt.");
            return Outcome.Cancel;
        }

        return AskYesNoReadMore(body);
    }

    /// <summary>Confirms a calendar event create that will dispatch invites.</summary>
    public static Outcome CalendarCreate(
        string subject,
        string startStr,
        string endStr,
        string timezone,
        string[] attendees,
        string[] optionalAttendees,
        string? body,
        bool autoYes,
        int previewLines = 20)
    {
        if (autoYes) return Outcome.Send;

        PrintCalendarHeader(subject, startStr, endStr, timezone, attendees, optionalAttendees);
        PrintBodyPreview(body ?? "", previewLines);

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("✗ Refusing to create: stdin is not a terminal.");
            Console.Error.WriteLine("  Re-run interactively, or pass --yes to bypass the prompt.");
            return Outcome.Cancel;
        }

        return AskYesNoReadMore(body ?? "");
    }

    private static Outcome AskYesNoReadMore(string fullBody)
    {
        while (true)
        {
            Console.Error.Write("\n[Y]es / [N]o / [R]ead more: ");
            var input = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            switch (input)
            {
                case "y": case "yes": case "sí": case "si":
                    return Outcome.Send;
                case "n": case "no": case "":
                    return Outcome.Cancel;
                case "r": case "read": case "more": case "read more":
                    PrintFullBody(fullBody);
                    Console.Error.Write("\n[Y]es / [N]o: ");
                    var input2 = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    return input2 is "y" or "yes" or "sí" or "si" ? Outcome.Send : Outcome.Cancel;
                default:
                    Console.Error.WriteLine($"  Unrecognized: '{input}'. Answer Y / N / R.");
                    continue;
            }
        }
    }

    private static void PrintEmailHeader(string verb, string[] to, string[] cc, string subject)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"─── about to {verb} ───");
        Console.Error.WriteLine($"To:      {string.Join(", ", to)}");
        if (cc.Length > 0)
            Console.Error.WriteLine($"Cc:      {string.Join(", ", cc)}");
        Console.Error.WriteLine($"Subject: {subject}");
    }

    private static void PrintCalendarHeader(
        string subject, string startStr, string endStr, string timezone,
        string[] attendees, string[] optionalAttendees)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("─── about to create calendar event (sends invites) ───");
        Console.Error.WriteLine($"Subject:   {subject}");
        Console.Error.WriteLine($"Start:     {startStr} ({timezone})");
        Console.Error.WriteLine($"End:       {endStr} ({timezone})");
        if (attendees.Length > 0)
            Console.Error.WriteLine($"Attendees: {string.Join(", ", attendees)}");
        if (optionalAttendees.Length > 0)
            Console.Error.WriteLine($"Optional:  {string.Join(", ", optionalAttendees)}");
    }

    private static void PrintBodyPreview(string body, int lines)
    {
        if (string.IsNullOrEmpty(body))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("(empty body)");
            return;
        }
        Console.Error.WriteLine();
        var bodyLines = body.Replace("\r\n", "\n").Split('\n');
        var take = Math.Min(lines, bodyLines.Length);
        for (int i = 0; i < take; i++)
            Console.Error.WriteLine(bodyLines[i]);
        if (bodyLines.Length > lines)
            Console.Error.WriteLine($"... ({bodyLines.Length - lines} more line(s) — choose [R]ead more to view full body)");
    }

    private static void PrintFullBody(string body)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("─── full body ───");
        Console.Error.WriteLine(string.IsNullOrEmpty(body) ? "(empty)" : body);
        Console.Error.WriteLine("─── end ───");
    }
}
