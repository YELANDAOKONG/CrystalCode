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
            mouseReporting: true,
            titlePushed: true);

        Assert.Equal(
            ["\u001b[23;0t", "\u001b[?1006l", "\u001b[?1000l", "\u001b[?2004l", "\u001b[?1049l"],
            codes);
    }

    [Fact]
    public void RecoverySequences_LeavesModesThatWereNotEnabled()
    {
        var codes = AlternateScreen.RecoverySequences(
            alternateBuffer: true,
            bracketedPaste: true,
            mouseReporting: false,
            titlePushed: false);

        Assert.Equal(["\u001b[?2004l", "\u001b[?1049l"], codes);
    }

    [Fact]
    public void LeaveSequences_ShowsTheCursorAfterThePrimaryScreenReturns()
    {
        var codes = AlternateScreen.LeaveSequences();

        Assert.Equal("\u001b[?1049l", codes[^2]);
        Assert.Equal("\u001b[?25h", codes[^1]);
    }
}
