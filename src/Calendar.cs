using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Graph.Me.Calendar.GetSchedule;
using Microsoft.Graph.Me.Events.Item.Accept;
using Microsoft.Graph.Me.Events.Item.Decline;
using Microsoft.Graph.Me.Events.Item.TentativelyAccept;
using Microsoft.Graph.Models;

namespace MailTool;

/// <summary>
/// Microsoft 365 calendar operations: create, list, show, and delete events on
/// the authenticated user's primary calendar. Online meetings (Teams) are
/// supported via the <c>--online</c> flag and require no additional scope
/// beyond Calendars.ReadWrite.
/// </summary>
public static class Calendar
{
    /// <summary>Creates a calendar event. Attendees receive an invite automatically.</summary>
    public static async Task CreateAsync(
        string subject,
        string startStr,
        string endStr,
        string timezone,
        string[] attendees,
        string[] optionalAttendees,
        string? body,
        string? location,
        bool online,
        bool autoYes,
        CancellationToken ct)
    {
        // A create with attendees triggers Graph to send invites — same blast
        // radius as an outbound mail. Gate it through the same confirmation.
        // Events with no attendees skip the prompt (purely local calendar entry).
        if (attendees.Length > 0 || optionalAttendees.Length > 0)
        {
            if (Confirm.CalendarCreate(subject, startStr, endStr, timezone, attendees, optionalAttendees, body, autoYes) == Confirm.Outcome.Cancel)
            {
                Console.Error.WriteLine("Cancelled — event not created.");
                Environment.Exit(1);
                return;
            }
        }

        var client = await Auth.GetClientAsync(ct);

        var ev = new Event
        {
            Subject = subject,
            Start = new DateTimeTimeZone { DateTime = NormalizeDateTime(startStr), TimeZone = timezone },
            End   = new DateTimeTimeZone { DateTime = NormalizeDateTime(endStr),   TimeZone = timezone },
        };

        if (!string.IsNullOrEmpty(body))
            ev.Body = new ItemBody { ContentType = BodyType.Text, Content = body };

        if (!string.IsNullOrEmpty(location))
            ev.Location = new Microsoft.Graph.Models.Location { DisplayName = location };

        var attendeeList = new List<Attendee>();
        foreach (var a in attendees)
            attendeeList.Add(new Attendee
            {
                EmailAddress = new EmailAddress { Address = a.Trim() },
                Type = AttendeeType.Required
            });
        foreach (var a in optionalAttendees)
            attendeeList.Add(new Attendee
            {
                EmailAddress = new EmailAddress { Address = a.Trim() },
                Type = AttendeeType.Optional
            });
        if (attendeeList.Count > 0) ev.Attendees = attendeeList;

        if (online)
        {
            ev.IsOnlineMeeting = true;
            ev.OnlineMeetingProvider = OnlineMeetingProviderType.TeamsForBusiness;
        }

        var created = await client.Me.Events.PostAsync(ev, cancellationToken: ct);

        Console.Error.WriteLine($"Created event: {created?.Subject}");
        Console.Error.WriteLine($"  id:    {created?.Id}");
        Console.Error.WriteLine($"  start: {created?.Start?.DateTime} ({created?.Start?.TimeZone})");
        Console.Error.WriteLine($"  end:   {created?.End?.DateTime} ({created?.End?.TimeZone})");
        if (created?.OnlineMeeting?.JoinUrl is string join && !string.IsNullOrEmpty(join))
            Console.Error.WriteLine($"  join:  {join}");
        if (created?.WebLink is string web && !string.IsNullOrEmpty(web))
            Console.Error.WriteLine($"  web:   {web}");
    }

    /// <summary>
    /// Pulls events into the local cache for [now − daysBack, now + daysAhead].
    /// Replaces any prior cached events (full refresh, not delta).
    /// </summary>
    public static async Task SyncAsync(int daysBack, int daysAhead, CancellationToken ct)
    {
        Storage.EnsureDirs();
        var client = await Auth.GetClientAsync(ct);
        var start = DateTimeOffset.UtcNow.AddDays(-daysBack);
        var end   = DateTimeOffset.UtcNow.AddDays(daysAhead);

        // Fresh write: clear and rebuild the events folder + index.
        if (Directory.Exists(Storage.EventsDir))
            Directory.Delete(Storage.EventsDir, recursive: true);
        Directory.CreateDirectory(Storage.EventsDir);

        var idx = new EventsIndex
        {
            WindowStart = start,
            WindowEnd = end,
            LastSync = DateTimeOffset.UtcNow
        };

        int n = 0;
        var page = await client.Me.CalendarView.GetAsync(cfg =>
        {
            cfg.QueryParameters.StartDateTime = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
            cfg.QueryParameters.EndDateTime   = end.ToString("yyyy-MM-ddTHH:mm:ssZ");
            cfg.QueryParameters.Top = 200;
            cfg.QueryParameters.Orderby = ["start/dateTime"];
        }, cancellationToken: ct);

        while (page is not null)
        {
            foreach (var e in page.Value ?? [])
            {
                if (e.Id is null) continue;
                var startDt = ParseEventStart(e);
                var path = Storage.EventPath(startDt, e.Id);
                File.WriteAllText(path, EventToJson(e).ToJsonString(Storage.JsonOpts));
                idx.ById[e.Id] = Path.GetRelativePath(Storage.CacheRoot, path);
                n++;
            }

            if (string.IsNullOrEmpty(page.OdataNextLink)) break;
            page = await client.Me.CalendarView
                .WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: ct);
        }

        Storage.SaveEventsIndex(idx);
        Console.Error.WriteLine($"Synced {n} events ({start:yyyy-MM-dd} → {end:yyyy-MM-dd}).");
    }

    /// <summary>
    /// Lists events. Reads from local cache by default (run <c>calendar sync</c> first);
    /// pass <paramref name="live"/> to bypass cache and hit Graph directly.
    /// View modes: <c>agenda</c> (default chronological), <c>day</c>, <c>week</c>.
    /// </summary>
    public static async Task ListAsync(
        int daysBack,
        int daysAhead,
        bool json,
        bool live,
        string viewMode,
        string? dateStr,
        CancellationToken ct)
    {
        List<JsonObject> items;
        if (live)
        {
            var client = await Auth.GetClientAsync(ct);
            var start = DateTimeOffset.UtcNow.AddDays(-daysBack);
            var end   = DateTimeOffset.UtcNow.AddDays(daysAhead);
            var view = await client.Me.CalendarView.GetAsync(cfg =>
            {
                cfg.QueryParameters.StartDateTime = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
                cfg.QueryParameters.EndDateTime   = end.ToString("yyyy-MM-ddTHH:mm:ssZ");
                cfg.QueryParameters.Top = 200;
                cfg.QueryParameters.Orderby = ["start/dateTime"];
            }, cancellationToken: ct);
            items = (view?.Value ?? []).Select(EventToJson).ToList();
        }
        else
        {
            items = LoadCachedEvents();
            if (items.Count == 0)
                Console.Error.WriteLine("(no cached events — run `mailtool calendar sync` first, or pass --live)");
        }

        // Filter by view mode.
        DateTime anchor = string.IsNullOrEmpty(dateStr)
            ? DateTime.Today
            : DateTime.Parse(dateStr, CultureInfo.InvariantCulture);

        switch (viewMode)
        {
            case "day":
                items = items.Where(e => StartLocal(e).Date == anchor.Date).ToList();
                break;
            case "week":
            {
                var monday = anchor.AddDays(-(int)((((int)anchor.DayOfWeek) + 6) % 7)).Date;
                var sunday = monday.AddDays(7);
                items = items.Where(e => StartLocal(e) >= monday && StartLocal(e) < sunday).ToList();
                break;
            }
            case "agenda":
            default:
                // Honor daysBack/daysAhead window for cached reads
                if (!live)
                {
                    var winStart = DateTimeOffset.UtcNow.AddDays(-daysBack);
                    var winEnd   = DateTimeOffset.UtcNow.AddDays(daysAhead);
                    items = items.Where(e =>
                    {
                        var s = StartUtc(e);
                        return s >= winStart && s <= winEnd;
                    }).ToList();
                }
                break;
        }

        items = items.OrderBy(StartUtc).ToList();

        if (json)
        {
            var arr = new JsonArray();
            foreach (var e in items) arr.Add(JsonNode.Parse(e.ToJsonString())!);
            Console.WriteLine(arr.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (items.Count == 0) { Console.WriteLine("(no events)"); return; }

        switch (viewMode)
        {
            case "day":
                RenderDay(items, anchor.Date);
                break;
            case "week":
            {
                var monday = anchor.AddDays(-(int)((((int)anchor.DayOfWeek) + 6) % 7)).Date;
                RenderWeek(items, monday);
                break;
            }
            case "agenda":
            default:
                RenderAgenda(items);
                break;
        }
    }

    // ---- view renderers ----------------------------------------------------

    private static void RenderAgenda(List<JsonObject> items)
    {
        DateTime? lastDay = null;
        foreach (var e in items)
        {
            var s = StartLocal(e);
            if (lastDay != s.Date)
            {
                Console.WriteLine();
                Console.WriteLine($"━━ {s:dddd, MMM d yyyy} ━━");
                lastDay = s.Date;
            }
            var subj = e["subject"]?.GetValue<string>() ?? "(no subject)";
            var endTime = EndLocal(e);
            var loc = e["location"]?.GetValue<string>() ?? "";
            var locStr = string.IsNullOrEmpty(loc) ? "" : $"  @ {loc}";
            var join = e["joinUrl"]?.GetValue<string>();
            var joinFlag = string.IsNullOrEmpty(join) ? "  " : "📞";
            Console.WriteLine($"  {s:HH:mm}–{endTime:HH:mm}  {joinFlag} {subj}{locStr}");
            Console.WriteLine($"            id: {e["id"]?.GetValue<string>()}");
        }
    }

    private static void RenderDay(List<JsonObject> items, DateTime day)
    {
        Console.WriteLine($"━━ {day:dddd, MMM d yyyy} ━━");
        if (items.Count == 0) { Console.WriteLine("  (no events)"); return; }
        foreach (var e in items)
        {
            var s = StartLocal(e);
            var endTime = EndLocal(e);
            var subj = e["subject"]?.GetValue<string>() ?? "(no subject)";
            var loc = e["location"]?.GetValue<string>() ?? "";
            var locStr = string.IsNullOrEmpty(loc) ? "" : $"  @ {loc}";
            var join = e["joinUrl"]?.GetValue<string>();
            var joinFlag = string.IsNullOrEmpty(join) ? "  " : "📞";
            Console.WriteLine($"  {s:HH:mm}–{endTime:HH:mm}  {joinFlag} {subj}{locStr}");
            Console.WriteLine($"            id: {e["id"]?.GetValue<string>()}");
        }
    }

    private static void RenderWeek(List<JsonObject> items, DateTime monday)
    {
        var byDay = items.GroupBy(e => StartLocal(e).Date).ToDictionary(g => g.Key, g => g.OrderBy(StartLocal).ToList());
        for (int i = 0; i < 7; i++)
        {
            var d = monday.AddDays(i);
            Console.WriteLine();
            Console.WriteLine($"━━ {d:dddd, MMM d} ━━");
            if (!byDay.TryGetValue(d, out var dayItems) || dayItems.Count == 0)
            {
                Console.WriteLine("  (free)");
                continue;
            }
            foreach (var e in dayItems)
            {
                var s = StartLocal(e);
                var endTime = EndLocal(e);
                var subj = e["subject"]?.GetValue<string>() ?? "(no subject)";
                var join = e["joinUrl"]?.GetValue<string>();
                var joinFlag = string.IsNullOrEmpty(join) ? "  " : "📞";
                Console.WriteLine($"  {s:HH:mm}–{endTime:HH:mm}  {joinFlag} {subj}");
            }
        }
    }

    // ---- cache helpers -----------------------------------------------------

    private static List<JsonObject> LoadCachedEvents()
    {
        var idx = Storage.LoadEventsIndex();
        var result = new List<JsonObject>();
        foreach (var (id, rel) in idx.ById)
        {
            var e = Storage.LoadEvent(rel);
            if (e is not null) result.Add(e);
        }
        return result;
    }

    private static DateTime StartLocal(JsonObject e)
    {
        var s = e["start"]?["dateTime"]?.GetValue<string>();
        var tz = e["start"]?["timeZone"]?.GetValue<string>() ?? "UTC";
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return DateTime.MinValue;
        return ConvertToLocal(dt, tz);
    }

    private static DateTime EndLocal(JsonObject e)
    {
        var s = e["end"]?["dateTime"]?.GetValue<string>();
        var tz = e["end"]?["timeZone"]?.GetValue<string>() ?? "UTC";
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return DateTime.MinValue;
        return ConvertToLocal(dt, tz);
    }

    private static DateTimeOffset StartUtc(JsonObject e)
    {
        var local = StartLocal(e);
        if (local == DateTime.MinValue) return DateTimeOffset.MinValue;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }

    private static DateTime ConvertToLocal(DateTime naive, string tzName)
    {
        try
        {
            var src = TimeZoneInfo.FindSystemTimeZoneById(tzName);
            return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), src, TimeZoneInfo.Local);
        }
        catch
        {
            return naive;  // best-effort if tz not found
        }
    }

    private static DateTimeOffset ParseEventStart(Event e)
    {
        var s = e.Start?.DateTime;
        return DateTimeOffset.TryParse(s, out var d) ? d : DateTimeOffset.UtcNow;
    }

    /// <summary>Prints a single event's full details.</summary>
    public static async Task ShowAsync(string id, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        var e = await client.Me.Events[id].GetAsync(cancellationToken: ct)
                ?? throw new InvalidOperationException($"Event not found: {id}");
        Console.WriteLine(EventToJson(e).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Deletes an event by id. Sends cancellations to attendees automatically.</summary>
    public static async Task DeleteAsync(string id, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        await client.Me.Events[id].DeleteAsync(cancellationToken: ct);
        Console.Error.WriteLine($"Deleted event: {id}");
    }

    /// <summary>
    /// Updates an existing event. Only the fields you pass are changed; everything else stays.
    /// Pass <paramref name="addAttendees"/> to extend the attendee list, or <paramref name="attendees"/>
    /// to replace it wholesale.
    /// </summary>
    public static async Task UpdateAsync(
        string id,
        string? subject,
        string? startStr,
        string? endStr,
        string? timezone,
        string[] attendees,
        string[] addAttendees,
        string[] addOptional,
        string? body,
        string? location,
        bool? online,
        CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);

        var patch = new Event();
        bool any = false;

        if (!string.IsNullOrEmpty(subject))      { patch.Subject = subject; any = true; }
        if (!string.IsNullOrEmpty(body))         { patch.Body = new ItemBody { ContentType = BodyType.Text, Content = body }; any = true; }
        if (!string.IsNullOrEmpty(location))     { patch.Location = new Microsoft.Graph.Models.Location { DisplayName = location }; any = true; }
        if (online is bool ol)
        {
            patch.IsOnlineMeeting = ol;
            if (ol) patch.OnlineMeetingProvider = OnlineMeetingProviderType.TeamsForBusiness;
            any = true;
        }

        if (!string.IsNullOrEmpty(startStr) || !string.IsNullOrEmpty(endStr))
        {
            // Need both for a clean reschedule; fetch current to fill any missing piece.
            var current = await client.Me.Events[id].GetAsync(cancellationToken: ct)
                          ?? throw new InvalidOperationException($"Event not found: {id}");
            var tz = timezone ?? current.Start?.TimeZone ?? "UTC";
            patch.Start = new DateTimeTimeZone
            {
                DateTime = string.IsNullOrEmpty(startStr) ? current.Start?.DateTime : NormalizeDateTime(startStr),
                TimeZone = tz
            };
            patch.End = new DateTimeTimeZone
            {
                DateTime = string.IsNullOrEmpty(endStr) ? current.End?.DateTime : NormalizeDateTime(endStr),
                TimeZone = tz
            };
            any = true;
        }

        // Attendee semantics:
        //   --attendees X         → replace the whole list with required = X
        //   --add-attendees X     → keep existing, append X as required
        //   --add-optional X      → keep existing, append X as optional
        if (attendees.Length > 0)
        {
            patch.Attendees = attendees.Select(a => new Attendee
            {
                EmailAddress = new EmailAddress { Address = a.Trim() },
                Type = AttendeeType.Required
            }).ToList();
            any = true;
        }
        else if (addAttendees.Length > 0 || addOptional.Length > 0)
        {
            var current = await client.Me.Events[id].GetAsync(cancellationToken: ct)
                          ?? throw new InvalidOperationException($"Event not found: {id}");
            var merged = current.Attendees?.ToList() ?? new List<Attendee>();
            foreach (var a in addAttendees)
                merged.Add(new Attendee { EmailAddress = new EmailAddress { Address = a.Trim() }, Type = AttendeeType.Required });
            foreach (var a in addOptional)
                merged.Add(new Attendee { EmailAddress = new EmailAddress { Address = a.Trim() }, Type = AttendeeType.Optional });
            patch.Attendees = merged;
            any = true;
        }

        if (!any)
        {
            Console.Error.WriteLine("Nothing to update — pass at least one of --subject / --start / --end / --attendees / --add-attendees / --add-optional / --body / --location / --online / --no-online.");
            Environment.Exit(2);
            return;
        }

        var updated = await client.Me.Events[id].PatchAsync(patch, cancellationToken: ct);
        Console.Error.WriteLine($"Updated event: {updated?.Subject}");
        Console.Error.WriteLine($"  start: {updated?.Start?.DateTime} ({updated?.Start?.TimeZone})");
        Console.Error.WriteLine($"  end:   {updated?.End?.DateTime} ({updated?.End?.TimeZone})");
    }

    /// <summary>Accepts, declines, or tentatively accepts a meeting invite. Optionally sends a comment.</summary>
    public static async Task RespondAsync(string id, string action, string? comment, bool sendResponse, CancellationToken ct)
    {
        var client = await Auth.GetClientAsync(ct);
        switch (action)
        {
            case "accept":
                await client.Me.Events[id].Accept.PostAsync(
                    new AcceptPostRequestBody { Comment = comment, SendResponse = sendResponse },
                    cancellationToken: ct);
                Console.Error.WriteLine($"Accepted: {id}");
                break;
            case "decline":
                await client.Me.Events[id].Decline.PostAsync(
                    new DeclinePostRequestBody { Comment = comment, SendResponse = sendResponse },
                    cancellationToken: ct);
                Console.Error.WriteLine($"Declined: {id}");
                break;
            case "tentative":
                await client.Me.Events[id].TentativelyAccept.PostAsync(
                    new TentativelyAcceptPostRequestBody { Comment = comment, SendResponse = sendResponse },
                    cancellationToken: ct);
                Console.Error.WriteLine($"Tentatively accepted: {id}");
                break;
            default:
                throw new ArgumentException($"Unknown response action: {action}. Use accept/decline/tentative.");
        }
    }

    /// <summary>
    /// Returns free/busy availability for one or more email addresses over a window.
    /// Useful for proposing meeting times. External users (different tenant) may return
    /// "Unknown" if free/busy isn't federated.
    /// </summary>
    public static async Task AvailabilityAsync(
        string startStr,
        string endStr,
        string timezone,
        string[] schedules,
        int intervalMinutes,
        CancellationToken ct)
    {
        if (schedules.Length == 0)
            throw new ArgumentException("Pass at least one --attendees or --schedule address.");

        var client = await Auth.GetClientAsync(ct);
        var body = new GetSchedulePostRequestBody
        {
            Schedules = schedules.ToList(),
            StartTime = new DateTimeTimeZone { DateTime = NormalizeDateTime(startStr), TimeZone = timezone },
            EndTime   = new DateTimeTimeZone { DateTime = NormalizeDateTime(endStr),   TimeZone = timezone },
            AvailabilityViewInterval = intervalMinutes
        };

        var response = await client.Me.Calendar.GetSchedule.PostAsGetSchedulePostResponseAsync(body, cancellationToken: ct);
        var items = response?.Value ?? [];
        if (items.Count == 0) { Console.WriteLine("(no schedules returned)"); return; }

        // Header row showing each interval's start time.
        var startDt = DateTime.Parse(NormalizeDateTime(startStr), CultureInfo.InvariantCulture);
        var endDt   = DateTime.Parse(NormalizeDateTime(endStr),   CultureInfo.InvariantCulture);
        var slotCount = (int)Math.Ceiling((endDt - startDt).TotalMinutes / intervalMinutes);

        Console.WriteLine($"Window: {startStr} → {endStr} ({timezone}), {intervalMinutes}-min slots");
        Console.WriteLine($"Legend: 0=Free  1=Tentative  2=Busy  3=OOF  4=WorkElsewhere");
        Console.WriteLine();

        // Print column headers (time of each interval start).
        var headerLine1 = new string(' ', 38);
        var headerLine2 = new string(' ', 38);
        for (int i = 0; i < slotCount; i++)
        {
            var t = startDt.AddMinutes(i * intervalMinutes);
            // Show hour in 2-char form on a row, then minute markers if needed.
            headerLine1 += t.ToString("HH");
            headerLine2 += t.ToString("mm");
        }
        Console.WriteLine(headerLine1);
        Console.WriteLine(headerLine2);

        foreach (var s in items)
        {
            var who = s.ScheduleId ?? "(unknown)";
            var view = s.AvailabilityView ?? "";
            // Truncate name display to fit
            var nameCol = who.Length > 36 ? who[..35] + "…" : who.PadRight(36);
            Console.WriteLine($"{nameCol}  {view}");

            if (s.Error is not null)
                Console.WriteLine($"  [error: {s.Error.Message}]");
        }
    }

    /// <summary>
    /// Parses a human-friendly date/time string into the ISO-without-offset
    /// format Graph expects in <c>DateTimeTimeZone.dateTime</c>. The timezone is
    /// carried separately, so we must NOT shift the wall-clock time here.
    /// </summary>
    internal static string NormalizeDateTime(string input)
    {
        if (!DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            throw new ArgumentException($"Invalid date/time: '{input}'. Use formats like '2026-04-29 07:00' or '2026-04-29T07:00:00'.");
        return dt.ToString("yyyy-MM-ddTHH:mm:ss");
    }

    private static JsonObject EventToJson(Event e)
    {
        var attendeesArr = new JsonArray();
        if (e.Attendees is not null)
            foreach (var a in e.Attendees)
                attendeesArr.Add(new JsonObject
                {
                    ["address"] = a.EmailAddress?.Address,
                    ["name"] = a.EmailAddress?.Name,
                    ["type"] = a.Type?.ToString(),
                    ["status"] = a.Status?.Response?.ToString()
                });

        return new JsonObject
        {
            ["id"] = e.Id,
            ["subject"] = e.Subject,
            ["start"] = new JsonObject
            {
                ["dateTime"] = e.Start?.DateTime,
                ["timeZone"] = e.Start?.TimeZone
            },
            ["end"] = new JsonObject
            {
                ["dateTime"] = e.End?.DateTime,
                ["timeZone"] = e.End?.TimeZone
            },
            ["location"] = e.Location?.DisplayName,
            ["isOnlineMeeting"] = e.IsOnlineMeeting,
            ["joinUrl"] = e.OnlineMeeting?.JoinUrl,
            ["webLink"] = e.WebLink,
            ["attendees"] = attendeesArr,
            ["body"] = e.Body?.Content
        };
    }
}
