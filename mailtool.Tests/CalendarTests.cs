using MailTool;
using Xunit;

namespace MailTool.Tests;

public class CalendarTests
{
    // NormalizeDateTime: Graph's DateTimeTimeZone wants "yyyy-MM-ddTHH:mm:ss"
    // with NO offset/Z suffix, because the timezone is carried separately.
    // Shifting the wall-clock time during parse would create silent
    // off-by-N-hours bugs when the user passes "07:00" and expects 07:00.

    [Fact]
    public void NormalizeDateTime_SpaceSeparator_ReturnsIsoFormat()
    {
        Assert.Equal("2026-04-29T07:00:00", Calendar.NormalizeDateTime("2026-04-29 07:00"));
    }

    [Fact]
    public void NormalizeDateTime_TSeparator_ReturnsIsoFormat()
    {
        Assert.Equal("2026-04-29T07:00:00", Calendar.NormalizeDateTime("2026-04-29T07:00:00"));
    }

    [Fact]
    public void NormalizeDateTime_WithSeconds_PreservesSeconds()
    {
        Assert.Equal("2026-04-29T07:30:45", Calendar.NormalizeDateTime("2026-04-29T07:30:45"));
    }

    [Fact]
    public void NormalizeDateTime_DateOnly_DefaultsMidnight()
    {
        Assert.Equal("2026-04-29T00:00:00", Calendar.NormalizeDateTime("2026-04-29"));
    }

    [Fact]
    public void NormalizeDateTime_NoOffsetSuffix()
    {
        // The result must NOT carry a Z or offset — that would tell Graph
        // the time is already in UTC and override our --timezone flag.
        var result = Calendar.NormalizeDateTime("2026-04-29 14:00");
        Assert.DoesNotContain("Z", result);
        Assert.DoesNotContain("+", result);
    }

    [Fact]
    public void NormalizeDateTime_Invalid_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            Calendar.NormalizeDateTime("not a date"));
    }

    [Fact]
    public void NormalizeDateTime_PreservesWallClock_NotShiftedToUtc()
    {
        // Regression guard: ensure parse does not interpret input as local time
        // and convert. A user typing "07:00" must get "07:00:00" back regardless
        // of the host machine's timezone.
        var result = Calendar.NormalizeDateTime("2026-04-29 07:00");
        Assert.StartsWith("2026-04-29T07:", result);
    }
}
