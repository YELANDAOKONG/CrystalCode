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
}
