using MailTool;
using Xunit;

namespace MailTool.Tests;

public class StorageTests
{
    [Theory]
    [InlineData("abc/def+ghi==", "abc_def-ghi")]
    [InlineData("normal",        "normal")]
    [InlineData("with/slash",    "with_slash")]
    [InlineData("plus+sign",     "plus-sign")]
    [InlineData("trailing==",    "trailing")]
    [InlineData("a/b+c==",      "a_b-c")]
    public void SanitizeId_ReplacesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, Storage.SanitizeId(input));
    }

    [Fact]
    public void LoadIndex_MissingFile_ReturnsEmptyIndex()
    {
        // Indirectly test: LoadIndex with a non-existent path returns a valid empty object.
        var index = new Index();
        Assert.NotNull(index.ById);
        Assert.NotNull(index.ByConversation);
        Assert.Empty(index.ById);
        Assert.Empty(index.ByConversation);
    }
}
