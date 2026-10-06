using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

public sealed class TerminalTextTests
{
    [Fact]
    public void Sanitize_ReturnsTheSameStringWhenClean()
    {
        const string text = "hello 你好";

        Assert.Same(text, TerminalText.Sanitize(text));
    }

    [Fact]
    public void Sanitize_KeepsTextBeforeATrailingCarriageReturn()
    {
        Assert.Equal("hello", TerminalText.Sanitize("hello\r"));
    }

    [Fact]
    public void SanitizeStream_HoldsASplitEscape()
    {
        var state = default(TerminalText.StreamState);

        Assert.Equal(string.Empty, TerminalText.SanitizeStream("\u001b[3", ref state));
        Assert.Equal("ok", TerminalText.SanitizeStream("1mok", ref state));
        Assert.True(state.IsIdle);
    }

    [Fact]
    public void SanitizeStream_ReturnsTheSameStringWhenClean()
    {
        const string text = "token";
        var state = default(TerminalText.StreamState);

        Assert.Same(text, TerminalText.SanitizeStream(text, ref state));
    }

    [Fact]
    public void SanitizeStream_ReturnsACleanCrlfChunkUnchanged()
    {
        const string text = "hello\r\nworld";
        var state = default(TerminalText.StreamState);

        Assert.Same(text, TerminalText.SanitizeStream(text, ref state));
        Assert.True(state.IsIdle);
    }

    [Fact]
    public void SanitizeStream_JoinsACarriageReturnSplitFromItsLineFeed()
    {
        var state = default(TerminalText.StreamState);
        const string tail = "\nworld";

        Assert.Equal("hello", TerminalText.SanitizeStream("hello\r", ref state));
        Assert.False(state.IsIdle);
        Assert.Same(tail, TerminalText.SanitizeStream(tail, ref state));
        Assert.True(state.IsIdle);
    }

    [Fact]
    public void SanitizeStream_EmitsAHeldCarriageReturnBeforeTheNextText()
    {
        var state = default(TerminalText.StreamState);

        Assert.Equal("hello", TerminalText.SanitizeStream("hello\r", ref state));
        Assert.Equal("\nworld", TerminalText.SanitizeStream("world", ref state));
        Assert.True(state.IsIdle);
    }

    [Fact]
    public void SanitizeStream_ResetDropsAHeldCarriageReturn()
    {
        var state = default(TerminalText.StreamState);
        const string world = "world";
        _ = TerminalText.SanitizeStream("hello\r", ref state);

        state.Reset();

        Assert.Same(world, TerminalText.SanitizeStream(world, ref state));
    }

    [Fact]
    public void Sanitize_RemovesC1Controls()
    {
        Assert.Equal("a31mbcd", TerminalText.Sanitize("a\u009b31mb\u0085c\u009dd"));
    }

    [Fact]
    public void SanitizeStream_RemovesC1Controls()
    {
        var state = default(TerminalText.StreamState);

        Assert.Equal("ab", TerminalText.SanitizeStream("a\u009bb", ref state));
    }

    [Fact]
    public void SanitizeLine_FlattensBreaksAndTabs()
    {
        Assert.Equal("first second  third", TerminalText.SanitizeLine("first\nsecond\t third"));
        Assert.Equal("clear  done", TerminalText.SanitizeLine("clear \u001b[2J done"));
    }

    [Fact]
    public void StripControls_KeepsTabsAndPrintableText()
    {
        Assert.Equal("a\tb", TerminalText.StripControls("a\u001b\tb\n\r\u007f\u009b"));
        const string clean = "plain 你好";
        Assert.Same(clean, TerminalText.StripControls(clean));
    }

    [Fact]
    public void HasControls_IgnoresTabsOnly()
    {
        Assert.False(TerminalText.HasControls("a\tb"));
        Assert.True(TerminalText.HasControls("a\nb"));
        Assert.True(TerminalText.HasControls("a\u0085b"));
    }

    [Fact]
    public void Reveal_SpellsOutControlsAndBidirectionalMarks()
    {
        Assert.Equal("echo safe\\x1b[1G\\x1b[2Kls", TerminalText.Reveal("echo safe\u001b[1G\u001b[2Kls"));
        Assert.Equal("a\\u202eb", TerminalText.Reveal("a\u202eb"));
        Assert.Equal("a\\u200bb", TerminalText.Reveal("a\u200bb"));
        Assert.Equal("a\\x9bb", TerminalText.Reveal("a\u009bb"));
        Assert.Equal("a\\x0db", TerminalText.Reveal("a\rb"));
    }

    [Fact]
    public void Reveal_KeepsLineFeedsTabsAndEmojiJoiners()
    {
        Assert.Equal("a\n\tb", TerminalText.Reveal("a\r\n\tb"));
        const string family = "\U0001F468\u200D\U0001F469";
        Assert.Same(family, TerminalText.Reveal(family));
    }
}
