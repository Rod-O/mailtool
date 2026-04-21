using System.Text.Json.Nodes;
using MailTool;
using Xunit;

namespace MailTool.Tests;

public class ShowTests
{
    // HtmlToText

    [Fact]
    public void HtmlToText_Empty_ReturnsEmpty()
    {
        Assert.Equal("", Show.HtmlToText(""));
    }

    [Fact]
    public void HtmlToText_PlainText_Unchanged()
    {
        Assert.Equal("Hello World", Show.HtmlToText("Hello World"));
    }

    [Fact]
    public void HtmlToText_StripsTags()
    {
        Assert.Equal("Hello World", Show.HtmlToText("<p>Hello World</p>"));
    }

    [Fact]
    public void HtmlToText_DecodesEntities()
    {
        var result = Show.HtmlToText("<p>Hello &amp; World &lt;here&gt;</p>");
        Assert.Contains("Hello & World <here>", result);
    }

    [Fact]
    public void HtmlToText_BreakTags_BecomeNewlines()
    {
        var result = Show.HtmlToText("Line one<br>Line two<br/>Line three");
        Assert.Contains("Line one\nLine two\nLine three", result);
    }

    [Fact]
    public void HtmlToText_RemovesScriptBlock()
    {
        var result = Show.HtmlToText("<script>alert('xss')</script><p>Safe</p>");
        Assert.Contains("Safe", result);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void HtmlToText_RemovesStyleBlock()
    {
        var result = Show.HtmlToText("<style>body { color: red; }</style><p>Text</p>");
        Assert.Contains("Text", result);
        Assert.DoesNotContain("color", result);
    }

    [Fact]
    public void HtmlToText_CollapsesExtraBlankLines()
    {
        var result = Show.HtmlToText("<p>A</p>\n\n\n\n<p>B</p>");
        Assert.DoesNotContain("\n\n\n", result);
    }

    // FormatAddress

    [Fact]
    public void FormatAddress_Null_ReturnsEmpty()
    {
        Assert.Equal("", Show.FormatAddress(null));
    }

    [Fact]
    public void FormatAddress_WithName_ReturnsNameAndAddress()
    {
        var node = JsonNode.Parse("{\"name\":\"Alice\",\"address\":\"alice@example.com\"}");
        Assert.Equal("Alice <alice@example.com>", Show.FormatAddress(node));
    }

    [Fact]
    public void FormatAddress_EmptyName_ReturnsAddressOnly()
    {
        var node = JsonNode.Parse("{\"name\":\"\",\"address\":\"alice@example.com\"}");
        Assert.Equal("alice@example.com", Show.FormatAddress(node));
    }

    [Fact]
    public void FormatAddress_NullName_ReturnsAddressOnly()
    {
        var node = JsonNode.Parse("{\"address\":\"alice@example.com\"}");
        Assert.Equal("alice@example.com", Show.FormatAddress(node));
    }

    // FormatAddressList

    [Fact]
    public void FormatAddressList_Null_ReturnsEmpty()
    {
        Assert.Equal("", Show.FormatAddressList(null));
    }

    [Fact]
    public void FormatAddressList_MultipleRecipients_CommaSeparated()
    {
        var node = JsonNode.Parse("[{\"name\":\"Alice\",\"address\":\"a@x.com\"},{\"name\":\"Bob\",\"address\":\"b@x.com\"}]");
        var result = Show.FormatAddressList(node);
        Assert.Equal("Alice <a@x.com>, Bob <b@x.com>", result);
    }

    [Fact]
    public void FormatAddressList_EmptyArray_ReturnsEmpty()
    {
        var node = JsonNode.Parse("[]");
        Assert.Equal("", Show.FormatAddressList(node));
    }
}
