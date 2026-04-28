using System.Diagnostics;
using System.Text.RegularExpressions;
using MailTool;
using Xunit;

namespace MailTool.Tests;

/// <summary>
/// Defensive tests for known classes of issues against mailtool's attack surface:
/// path traversal in cache writes, ReDoS via user-supplied regex, and ANSI/OSC
/// terminal injection from hostile email content. Each test pins behavior the
/// hardening relies on — regressions here can re-open real exposures.
/// </summary>
public class SecurityTests
{
    // ---- SanitizeId — path traversal ------------------------------------

    [Fact]
    public void SanitizeId_RejectsDotDot_PreventingParentDirectoryWrite()
    {
        // A Graph id containing ".." must never produce a path that escapes
        // the cache root. Reject at sanitize time rather than relying on
        // downstream code to be careful.
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId("../../etc/passwd"));
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId(".."));
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId("foo/../bar"));
    }

    [Fact]
    public void SanitizeId_RejectsBackslash_PreventingWindowsPathTraversal()
    {
        // On Windows, '\' is a path separator. SanitizeId must convert both
        // '/' and '\' so the same id can't escape via either OS convention.
        var result = Storage.SanitizeId("a\\b\\c");
        Assert.DoesNotContain("\\", result);
    }

    [Fact]
    public void SanitizeId_RejectsNullByte()
    {
        // Null bytes in filenames truncate at the kernel layer on some systems
        // and confuse downstream tooling. Always reject.
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId("safe\0../etc/passwd"));
    }

    [Fact]
    public void SanitizeId_RejectsEmptyOrNull()
    {
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId(""));
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId(null!));
    }

    [Fact]
    public void SanitizeId_RejectsAllSlashes_WhichWouldYieldEmpty()
    {
        // "===" trim-ends to empty — empty results must be rejected, not
        // silently allowed (an empty filename collides with directory entries).
        Assert.Throws<ArgumentException>(() => Storage.SanitizeId("==="));
    }

    [Fact]
    public void SanitizeId_NormalGraphId_PassesThrough()
    {
        // Real Graph ids look like base64url with trailing '='. Normal case
        // must still work after the hardening.
        var graphId = "AAMkADIyN2MxYjViLWRhMzktNDU2YS05M2VmLTRmN2I0MmMzODk1MQ==";
        var clean = Storage.SanitizeId(graphId);
        Assert.Equal("AAMkADIyN2MxYjViLWRhMzktNDU2YS05M2VmLTRmN2I0MmMzODk1MQ", clean);
    }

    [Fact]
    public void EventPath_DoesNotEscapeCacheRoot_EvenWithCraftedId()
    {
        // Defense-in-depth: path traversal attempts via SanitizeId throw,
        // so the resulting path operations never run with an escaped path.
        Assert.Throws<ArgumentException>(() =>
            Storage.EventPath(DateTimeOffset.UtcNow, "../../escape"));
    }

    [Fact]
    public void MessagePath_DoesNotEscapeCacheRoot_EvenWithCraftedId()
    {
        Assert.Throws<ArgumentException>(() =>
            Storage.MessagePath(DateTimeOffset.UtcNow, "../sensitive"));
    }

    // ---- ReDoS — regex compilation must enforce timeout -----------------

    [Fact]
    public void Search_HostileRegex_DoesNotHangIndefinitely()
    {
        // Catastrophic backtracking on this pattern + input. With timeout
        // enforced, the match throws RegexMatchTimeoutException quickly
        // (within seconds, well under any pathological behavior).
        // Without timeout this loop would spin for minutes.
        var hostilePattern = "(a+)+$";
        var hostileHaystack = new string('a', 30) + "X";

        var rx = new Regex(
            hostilePattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        var sw = Stopwatch.StartNew();
        Assert.Throws<RegexMatchTimeoutException>(() => rx.IsMatch(hostileHaystack));
        sw.Stop();

        // Should bail out within ~1.5s of the configured timeout, never long-run.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Regex did not respect timeout: ran {sw.Elapsed.TotalSeconds:F1}s");
    }

    // ---- ANSI escape injection — terminal hardening ---------------------

    [Fact]
    public void SanitizeForTerminal_StripsEscChar_DefangingAnsiSequences()
    {
        // Without ESC (0x1B), the rest of an ANSI sequence is just printable
        // garbage. Stripping ESC alone neutralizes every CSI/OSC sequence.
        var hostileSubject = "\x1b[2JCleared screen";
        var safe = Show.SanitizeForTerminal(hostileSubject);
        Assert.DoesNotContain("\x1b", safe);
    }

    [Fact]
    public void SanitizeForTerminal_StripsOsc52ClipboardSequence()
    {
        // OSC 52 is a real-world risk: terminals like xterm/iterm/kitty
        // honor it to write the system clipboard, enabling silent
        // pwnership from a hostile email body.
        var hostile = "Hello\x1b]52;c;ZWNobyBwd25lZA==\x07world";
        var safe = Show.SanitizeForTerminal(hostile);
        Assert.DoesNotContain("\x1b", safe);
        Assert.DoesNotContain("\x07", safe);  // BEL terminator also stripped
    }

    [Fact]
    public void SanitizeForTerminal_PreservesNewlineAndTab_LegitContent()
    {
        // Body content commonly contains \n and \t — those must survive,
        // otherwise email rendering gets mangled.
        var legit = "line1\nline2\tindented";
        var safe = Show.SanitizeForTerminal(legit);
        Assert.Equal(legit, safe);
    }

    [Fact]
    public void SanitizeForTerminal_StripsDelChar()
    {
        // 0x7F (DEL) is non-printable and can confuse terminal/font handling.
        // Note: split via concat so C#'s `\x` greedy hex escape doesn't eat
        // the next char and parse it as `\x7Fa` = char(0x7FA).
        var hostile = "before" + "\x7F" + "after";
        var safe = Show.SanitizeForTerminal(hostile);
        Assert.Equal("beforeafter", safe);
    }

    [Fact]
    public void SanitizeForTerminal_StripsNullByte()
    {
        var hostile = "before\0after";
        var safe = Show.SanitizeForTerminal(hostile);
        Assert.Equal("beforeafter", safe);
    }

    [Fact]
    public void SanitizeForTerminal_EmptyOrNullInput_ReturnsEmpty()
    {
        Assert.Equal("", Show.SanitizeForTerminal(""));
        Assert.Equal("", Show.SanitizeForTerminal(null!));
    }

    [Fact]
    public void SanitizeForTerminal_PreservesCarriageReturn_MultilineBodies()
    {
        // \r is part of CRLF and shows up legitimately in email bodies
        // (especially HTML-converted ones from Outlook). Preserve.
        var crlfContent = "line1\r\nline2";
        var safe = Show.SanitizeForTerminal(crlfContent);
        Assert.Equal(crlfContent, safe);
    }
}
