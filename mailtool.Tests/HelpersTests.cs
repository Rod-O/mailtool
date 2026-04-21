using MailTool;
using Xunit;

namespace MailTool.Tests;

public class HelpersTests
{
    [Fact]
    public void ResolveId_ExactMatch_ReturnsId()
    {
        var index = new Index();
        index.ById["ABC123"] = "messages/2026/01/ABC123.json";
        Assert.Equal("ABC123", Helpers.ResolveId("ABC123", index));
    }

    [Fact]
    public void ResolveId_UniquePrefix_ReturnsFullId()
    {
        var index = new Index();
        index.ById["ABC123XYZ"] = "messages/2026/01/ABC123XYZ.json";
        Assert.Equal("ABC123XYZ", Helpers.ResolveId("ABC123", index));
    }

    [Fact]
    public void ResolveId_AmbiguousPrefix_ReturnsNull()
    {
        var index = new Index();
        index.ById["ABC123A"] = "a.json";
        index.ById["ABC123B"] = "b.json";
        Assert.Null(Helpers.ResolveId("ABC123", index));
    }

    [Fact]
    public void ResolveId_NoMatch_ReturnsNull()
    {
        var index = new Index();
        index.ById["XYZ999"] = "x.json";
        Assert.Null(Helpers.ResolveId("ABC123", index));
    }

    [Fact]
    public void ResolveId_EmptyIndex_ReturnsNull()
    {
        Assert.Null(Helpers.ResolveId("ABC", new Index()));
    }

    [Theory]
    [InlineData(".pdf",  "application/pdf")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".png",  "image/png")]
    [InlineData(".jpg",  "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".txt",  "text/plain")]
    [InlineData(".csv",  "text/csv")]
    [InlineData(".zip",  "application/octet-stream")]
    [InlineData(".bin",  "application/octet-stream")]
    public void MimeType_ReturnsCorrectType(string ext, string expected)
    {
        Assert.Equal(expected, Helpers.MimeType($"file{ext}"));
    }

    [Fact]
    public void MimeType_UppercaseExtension_NormalizesCorrectly()
    {
        Assert.Equal("image/png", Helpers.MimeType("FILE.PNG"));
    }
}
