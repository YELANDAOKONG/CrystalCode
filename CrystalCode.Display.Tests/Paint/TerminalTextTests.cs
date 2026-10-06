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
}
