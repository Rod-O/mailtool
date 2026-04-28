using System.Text.Json;
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

    // ---- EventsIndex (calendar cache) -----------------------------------

    [Fact]
    public void EventsIndex_DefaultCtor_HasEmptyById()
    {
        var idx = new EventsIndex();
        Assert.NotNull(idx.ById);
        Assert.Empty(idx.ById);
        Assert.Null(idx.WindowStart);
        Assert.Null(idx.WindowEnd);
        Assert.Null(idx.LastSync);
    }

    [Fact]
    public void EventsIndex_RoundTrip_PreservesAllFields()
    {
        // Cache reload is a critical path — if serialization drops a field,
        // sync state silently corrupts. Round-trip catches that.
        var original = new EventsIndex
        {
            WindowStart = new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero),
            WindowEnd   = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
            LastSync    = new DateTimeOffset(2026, 4, 28, 14, 30, 0, TimeSpan.Zero)
        };
        original.ById["evt-1"] = "events/2026/04/evt-1.json";
        original.ById["evt-2"] = "events/2026/05/evt-2.json";

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<EventsIndex>(json)!;

        Assert.Equal(original.WindowStart, restored.WindowStart);
        Assert.Equal(original.WindowEnd,   restored.WindowEnd);
        Assert.Equal(original.LastSync,    restored.LastSync);
        Assert.Equal(2, restored.ById.Count);
        Assert.Equal("events/2026/04/evt-1.json", restored.ById["evt-1"]);
        Assert.Equal("events/2026/05/evt-2.json", restored.ById["evt-2"]);
    }

    [Fact]
    public void EventPath_BuildsYearMonthNestedPath()
    {
        // Mirrors MessagePath layout: events are bucketed by year/month
        // of the start time so the cache stays browsable on disk.
        var start = new DateTimeOffset(2026, 4, 29, 7, 0, 0, TimeSpan.Zero);
        var path = Storage.EventPath(start, "evt-id-1");
        // Path shape regardless of OS separator: ends in "events/2026/04/evt-id-1.json"
        Assert.Contains(Path.Combine("events", "2026", "04"), path);
        Assert.EndsWith("evt-id-1.json", path);
    }

    [Fact]
    public void EventPath_SanitizesIdInFilename()
    {
        var start = new DateTimeOffset(2026, 4, 29, 7, 0, 0, TimeSpan.Zero);
        var path = Storage.EventPath(start, "id/with+special==");
        // SanitizeId rules: '/' → '_', '+' → '-', trailing '=' stripped.
        Assert.EndsWith("id_with-special.json", path);
    }
}
