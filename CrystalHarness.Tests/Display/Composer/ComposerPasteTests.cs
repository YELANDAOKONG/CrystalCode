using CrystalHarness.Display.Composer;

using Xunit;

namespace CrystalHarness.Tests.Display.Composer;

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
}
