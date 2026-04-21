using System.Text.Json.Nodes;
using MailTool;
using Xunit;

namespace MailTool.Tests;

public class SearchTests
{
    private static JsonObject MakeMessage(
        string from        = "sender@example.com",
        string fromName    = "Sender",
        string subject     = "Test Subject",
        string body        = "Hello World",
        string received    = "2026-01-15T10:00:00Z",
        string[]? to       = null)
    {
        var toArr = new JsonArray();
        foreach (var addr in to ?? ["recipient@example.com"])
            toArr.Add(JsonNode.Parse($"{{\"address\":\"{addr}\",\"name\":\"Recipient\"}}"));

        return new JsonObject
        {
            ["from"]             = new JsonObject { ["address"] = from, ["name"] = fromName },
            ["to"]               = toArr,
            ["subject"]          = subject,
            ["bodyPreview"]      = body,
            ["body"]             = new JsonObject { ["content"] = body, ["contentType"] = "text" },
            ["receivedDateTime"] = received
        };
    }

    [Fact]
    public void Matches_NoFilters_ReturnsTrue()
    {
        Assert.True(Search.Matches(MakeMessage(), new SearchOptions()));
    }

    [Fact]
    public void Matches_FromAddress_MatchesSubstring()
    {
        var msg = MakeMessage(from: "alice@example.com");
        Assert.True(Search.Matches(msg,  new SearchOptions { From = "alice" }));
        Assert.False(Search.Matches(msg, new SearchOptions { From = "bob" }));
    }

    [Fact]
    public void Matches_FromName_MatchesSubstring()
    {
        var msg = MakeMessage(fromName: "Alice Smith");
        Assert.True(Search.Matches(msg,  new SearchOptions { From = "smith" }));
        Assert.False(Search.Matches(msg, new SearchOptions { From = "jones" }));
    }

    [Fact]
    public void Matches_Subject_CaseInsensitive()
    {
        var msg = MakeMessage(subject: "Project Update");
        Assert.True(Search.Matches(msg,  new SearchOptions { Subject = "project" }));
        Assert.False(Search.Matches(msg, new SearchOptions { Subject = "invoice" }));
    }

    [Fact]
    public void Matches_To_MatchesRecipient()
    {
        var msg = MakeMessage(to: ["rod@coralvita.co"]);
        Assert.True(Search.Matches(msg,  new SearchOptions { To = "coralvita" }));
        Assert.False(Search.Matches(msg, new SearchOptions { To = "other.org" }));
    }

    [Fact]
    public void Matches_Since_ExcludesOlderMessages()
    {
        var msg  = MakeMessage(received: "2026-01-10T00:00:00Z");
        var opts = new SearchOptions { Since = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) };
        Assert.False(Search.Matches(msg, opts));
    }

    [Fact]
    public void Matches_Since_IncludesNewerMessages()
    {
        var msg  = MakeMessage(received: "2026-01-20T00:00:00Z");
        var opts = new SearchOptions { Since = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) };
        Assert.True(Search.Matches(msg, opts));
    }

    [Fact]
    public void Matches_Until_ExcludesNewerMessages()
    {
        var msg  = MakeMessage(received: "2026-01-20T00:00:00Z");
        var opts = new SearchOptions { Until = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) };
        Assert.False(Search.Matches(msg, opts));
    }

    [Fact]
    public void Matches_Query_MatchesSubjectAndPreview()
    {
        var msg = MakeMessage(subject: "Invoice March", body: "Please review");
        Assert.True(Search.Matches(msg,  new SearchOptions { Query = "invoice" }));
        Assert.True(Search.Matches(msg,  new SearchOptions { Query = "review" }));
        Assert.False(Search.Matches(msg, new SearchOptions { Query = "payment" }));
    }

    [Fact]
    public void Matches_Query_WithBodyMatch_SearchesBodyContent()
    {
        // bodyPreview is always searched; full body only when BodyMatch=true.
        // Put the token only in full body content, not in the preview.
        var msg = MakeMessage(subject: "Normal Subject", body: "Preview text");
        msg["body"] = new JsonObject
        {
            ["content"]     = "unique-token-xyz hidden in full body",
            ["contentType"] = "text"
        };
        Assert.True(Search.Matches(msg,  new SearchOptions { Query = "unique-token-xyz", BodyMatch = true }));
        Assert.False(Search.Matches(msg, new SearchOptions { Query = "unique-token-xyz", BodyMatch = false }));
    }

    [Fact]
    public void Matches_MultipleFilters_AllMustMatch()
    {
        var msg = MakeMessage(from: "alice@example.com", subject: "Budget");
        Assert.True(Search.Matches(msg,  new SearchOptions { From = "alice", Subject = "budget" }));
        Assert.False(Search.Matches(msg, new SearchOptions { From = "alice", Subject = "invoice" }));
        Assert.False(Search.Matches(msg, new SearchOptions { From = "bob",   Subject = "budget" }));
    }
}
