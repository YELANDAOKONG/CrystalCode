using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class AlternateScreenTests
{
    [Fact]
    public void RecoverySequences_RestoresEveryStepThatSucceeded()
    {
        var codes = AlternateScreen.RecoverySequences(
            alternateBuffer: true,
            bracketedPaste: true,
            alternateScroll: true,
            titlePushed: true);

        Assert.Equal(
            ["\u001b[23;0t", "\u001b[?1007l", "\u001b[?2004l", "\u001b[?1049l"],
            codes);
    }

    [Fact]
    public void RecoverySequences_LeavesModesThatWereNotEnabled()
    {
        var codes = AlternateScreen.RecoverySequences(
            alternateBuffer: true,
            bracketedPaste: true,
            alternateScroll: false,
            titlePushed: false);

        Assert.Equal(["\u001b[?2004l", "\u001b[?1049l"], codes);
    }
}
