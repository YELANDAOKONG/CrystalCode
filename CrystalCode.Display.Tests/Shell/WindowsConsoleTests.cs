using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class WindowsConsoleTests
{
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalInput = 0x0200;

    [Fact]
    public void ModeForReporting_ReleasesTheWheelAndKeepsProcessedInput()
    {
        var current = EnableProcessedInput | EnableQuickEditMode | EnableMouseInput;
        var desired = WindowsConsole.ModeForReporting(current);

        Assert.Equal(
            EnableProcessedInput | EnableExtendedFlags | EnableVirtualTerminalInput,
            desired);
    }

    [Fact]
    public void ModeForReporting_LeavesAnAlreadyReportingModeUnchanged()
    {
        var current = EnableProcessedInput | EnableExtendedFlags | EnableVirtualTerminalInput;
        Assert.Equal(current, WindowsConsole.ModeForReporting(current));
    }
}
