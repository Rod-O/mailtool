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
    public void ParseFolders_CustomId_PassedThrough()
    {
        var result = Args.ParseFolders(["--folder", "myCustomFolder"]);
        Assert.Equal(["mycustomfolder"], result);
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
}
