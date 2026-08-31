using CrystalHarness.Display.Composer;

using Xunit;

namespace CrystalHarness.Display.Tests.Composer;

public sealed class BracketedPasteTests
{
    [Fact]
    public void Push_CompleteMarkers_YieldsNormalizedText()
    {
        var paste = new BracketedPaste();
        var chunks = paste.Push(
            BracketedPaste.StartMarker + "a\r\nb" + BracketedPaste.EndMarker);

        Assert.Equal(["a\nb"], chunks);
        Assert.False(paste.IsOpen);
    }

    [Fact]
    public void Push_HoldsUntilEndMarker()
    {
        var paste = new BracketedPaste();

        Assert.Empty(paste.Push(BracketedPaste.StartMarker + "hel"));
        Assert.True(paste.IsOpen);
        Assert.Equal(["hello"], paste.Push("lo" + BracketedPaste.EndMarker));
        Assert.False(paste.IsOpen);
    }

    [Fact]
    public void Push_IgnoresTextBeforeStartMarker()
    {
        var paste = new BracketedPaste();
        var chunks = paste.Push(
            "noise" + BracketedPaste.StartMarker + "ok" + BracketedPaste.EndMarker);

        Assert.Equal(["ok"], chunks);
    }

    [Fact]
    public void Reset_ClearsOpenState()
    {
        var paste = new BracketedPaste();
        _ = paste.Push(BracketedPaste.StartMarker + "held");
        paste.Reset();

        Assert.False(paste.IsOpen);
        Assert.Empty(paste.Push("not paste"));
    }

    [Fact]
    public void Normalize_TurnsCarriageReturnIntoNewline()
    {
        Assert.Equal("a\nb\nc", BracketedPaste.Normalize("a\r\nb\rc"));
    }
}
