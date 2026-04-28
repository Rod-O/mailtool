using MailTool;
using Xunit;

namespace MailTool.Tests;

public class ArgsTests
{
    // ParseFlag

    [Fact]
    public void ParseFlag_Found_ReturnsValue()
    {
        Assert.Equal("hello world", Args.ParseFlag(["--body", "hello world", "--other", "x"], "--body"));
    }

    [Fact]
    public void ParseFlag_NotFound_ReturnsNull()
    {
        Assert.Null(Args.ParseFlag(["--other", "x"], "--body"));
    }

    [Fact]
    public void ParseFlag_FlagAtEnd_ReturnsNull()
    {
        Assert.Null(Args.ParseFlag(["--body"], "--body"));
    }

    // ParseMultiFlag

    [Fact]
    public void ParseMultiFlag_MultipleOccurrences_ReturnsAll()
    {
        var result = Args.ParseMultiFlag(["--attach", "a.pdf", "--attach", "b.pdf", "--body", "text"], "--attach");
        Assert.Equal(["a.pdf", "b.pdf"], result);
    }

    [Fact]
    public void ParseMultiFlag_NoOccurrence_ReturnsEmpty()
    {
        Assert.Empty(Args.ParseMultiFlag(["--body", "text"], "--attach"));
    }

    // ParsePages

    [Fact]
    public void ParsePages_FlagPresent_ReturnsValue()
    {
        Assert.Equal(5, Args.ParsePages(["--pages", "5"]));
    }

    [Fact]
    public void ParsePages_FlagAbsent_ReturnsDefault2()
    {
        Assert.Equal(2, Args.ParsePages([]));
    }

    // ParseFolders

    [Theory]
    [InlineData("inbox", new[] { "inbox" })]
    [InlineData("sent",  new[] { "sentitems" })]
    public void ParseFolders_NamedFolder_ReturnsMapped(string input, string[] expected)
    {
        Assert.Equal(expected, Args.ParseFolders(["--folder", input]));
    }

    [Fact]
    public void ParseFolders_All_ReturnsBothFolders()
    {
        Assert.Equal(["inbox", "sentitems"], Args.ParseFolders(["--folder", "all"]));
    }

    [Fact]
    public void ParseFolders_NoFlag_ReturnsBothDefault()
    {
        Assert.Equal(["inbox", "sentitems"], Args.ParseFolders([]));
    }

    [Fact]
    public void ParseFolders_CustomId_PreservesCase()
    {
        // Regression: prior implementation lowercased every input, breaking
        // case-sensitive Graph folder ids. Custom (non-alias) values must
        // pass through unchanged so raw ids work with sync/search/--in-folder.
        var rawId = "AAMkADIyN2MxYjVi-WRhMzktNDU2YS09";
        var result = Args.ParseFolders(["--folder", rawId]);
        Assert.Equal([rawId], result);
    }

    [Fact]
    public void ParseFolders_DisplayName_PreservesCase()
    {
        Assert.Equal(["Important"], Args.ParseFolders(["--folder", "Important"]));
    }

    // Calendar-flavor arg flags (consumed by `calendar create / list / availability`)

    [Fact]
    public void ParseFlag_TimezoneFlag_ReturnsValue()
    {
        Assert.Equal("America/New_York",
            Args.ParseFlag(["--start", "2026-04-29 07:00", "--timezone", "America/New_York"], "--timezone"));
    }

    [Fact]
    public void ParseFlag_ViewFlag_ReturnsValue()
    {
        Assert.Equal("week", Args.ParseFlag(["--view", "week", "--date", "2026-04-29"], "--view"));
    }

    [Fact]
    public void ParseMultiFlag_Attendees_AggregatesAll()
    {
        var result = Args.ParseMultiFlag(
            ["--attendees", "a@x.com", "--attendees", "b@x.com", "--attendees", "c@x.com"],
            "--attendees");
        Assert.Equal(["a@x.com", "b@x.com", "c@x.com"], result);
    }

    [Fact]
    public void ParseMultiFlag_AddAttendees_DistinctFromAttendees()
    {
        var args = new[] { "--attendees", "a@x.com", "--add-attendees", "b@x.com" };
        Assert.Equal(["a@x.com"], Args.ParseMultiFlag(args, "--attendees"));
        Assert.Equal(["b@x.com"], Args.ParseMultiFlag(args, "--add-attendees"));
    }

    [Fact]
    public void HasFlag_Online_True()
    {
        Assert.True(Args.HasFlag(["--subject", "x", "--online"], "--online"));
    }

    [Fact]
    public void HasFlag_NoOnline_DistinctFromOnline()
    {
        // --no-online and --online must be detectable independently
        var args = new[] { "--no-online" };
        Assert.False(Args.HasFlag(args, "--online"));
        Assert.True(Args.HasFlag(args, "--no-online"));
    }

    [Fact]
    public void HasFlag_Live_True()
    {
        Assert.True(Args.HasFlag(["--view", "agenda", "--live"], "--live"));
    }

    // ParseSearchOptions

    [Fact]
    public void ParseSearchOptions_FromFlag_SetsFrom()
    {
        Assert.Equal("alice@example.com", Args.ParseSearchOptions(["--from", "alice@example.com"]).From);
    }

    [Fact]
    public void ParseSearchOptions_ToFlag_SetsTo()
    {
        Assert.Equal("bob@example.com", Args.ParseSearchOptions(["--to", "bob@example.com"]).To);
    }

    [Fact]
    public void ParseSearchOptions_SubjectFlag_SetsSubject()
    {
        Assert.Equal("hello", Args.ParseSearchOptions(["--subject", "hello"]).Subject);
    }

    [Fact]
    public void ParseSearchOptions_SinceFlag_ParsesDate()
    {
        var opts = Args.ParseSearchOptions(["--since", "2026-01-01"]);
        Assert.Equal(2026, opts.Since!.Value.Year);
        Assert.Equal(1,    opts.Since!.Value.Month);
        Assert.Equal(1,    opts.Since!.Value.Day);
    }

    [Fact]
    public void ParseSearchOptions_LimitFlag_SetsLimit()
    {
        Assert.Equal(10, Args.ParseSearchOptions(["--limit", "10"]).Limit);
    }

    [Fact]
    public void ParseSearchOptions_BodyFlag_SetsBodyMatch()
    {
        Assert.True(Args.ParseSearchOptions(["--body"]).BodyMatch);
    }

    [Fact]
    public void ParseSearchOptions_PositionalArgs_JoinedAsQuery()
    {
        Assert.Equal("invoice march 2026", Args.ParseSearchOptions(["invoice", "march", "2026"]).Query);
    }

    [Fact]
    public void ParseSearchOptions_NoArgs_DefaultLimit50()
    {
        Assert.Equal(50, Args.ParseSearchOptions([]).Limit);
    }

    // New flags

    [Fact]
    public void ParseSearchOptions_SubjectMatch_SetsRegex()
    {
        Assert.Equal("maldives|tanfon", Args.ParseSearchOptions(["--subject-match", "maldives|tanfon"]).SubjectRegex);
    }

    [Fact]
    public void ParseSearchOptions_InFolder_SetsValue()
    {
        Assert.Equal("Inbox/purchases", Args.ParseSearchOptions(["--in-folder", "Inbox/purchases"]).InFolder);
    }

    [Fact]
    public void ParseSearchOptions_JsonFlag_SetsJson()
    {
        Assert.True(Args.ParseSearchOptions(["--json"]).Json);
    }

    [Fact]
    public void ParseSearchOptions_JsonFlagAbsent_DefaultsFalse()
    {
        Assert.False(Args.ParseSearchOptions([]).Json);
    }

    [Fact]
    public void HasFlag_Present_ReturnsTrue()
    {
        Assert.True(Args.HasFlag(["--dry-run", "--create"], "--dry-run"));
    }

    [Fact]
    public void HasFlag_Absent_ReturnsFalse()
    {
        Assert.False(Args.HasFlag(["--create"], "--dry-run"));
    }
}
