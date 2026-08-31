using CrystalHarness.Display.Composer;

using Xunit;

namespace CrystalHarness.Display.Tests.Composer;

public sealed class ComposerPasteTests
{
    [Fact]
    public void FromBurst_KeepsTextAndTurnsEnterIntoNewline()
    {
        var burst = new List<ConsoleKeyInfo>
        {
            new('a', ConsoleKey.A, false, false, false),
            new('\r', ConsoleKey.Enter, false, false, false),
            new('b', ConsoleKey.B, false, false, false),
            new('\u0001', ConsoleKey.A, false, false, true)
        };

        Assert.Equal("a\nb", ComposerPaste.FromBurst(burst));
    }

    [Fact]
    public void Chars_ReconstructsBracketedPasteMarkers()
    {
        var burst = new List<ConsoleKeyInfo>();
        foreach (var ch in BracketedPaste.StartMarker + "hi" + BracketedPaste.EndMarker)
        {
            var key = ch == '\u001b' ? ConsoleKey.Escape : ConsoleKey.None;
            burst.Add(new ConsoleKeyInfo(ch, key, false, false, false));
        }

        Assert.Equal(
            BracketedPaste.StartMarker + "hi" + BracketedPaste.EndMarker,
            ComposerPaste.Chars(burst));
    }

    [Fact]
    public void Chars_ReconstructsPasteMarkersWhenEscapeKeyIsEmpty()
    {
        var burst = new List<ConsoleKeyInfo>();
        foreach (var ch in BracketedPaste.StartMarker + "hello" + BracketedPaste.EndMarker)
        {
            burst.Add(new ConsoleKeyInfo(ch, default, false, false, false));
        }

        Assert.Equal(
            BracketedPaste.StartMarker + "hello" + BracketedPaste.EndMarker,
            ComposerPaste.Chars(burst));
    }
}
