using System.Runtime.InteropServices;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Turns on VT input on Windows so CSI sequences reach ReadKey.
/// ReadKey then reports Key empty for Tab/Enter; InputDecoder recovers them.
/// Quick edit and native mouse input are off so the console does not keep the wheel.
/// </summary>
internal static class WindowsConsole
{
    private const int StandardInputHandle = -10;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalInput = 0x0200;

    /// <summary>
    /// VT input delivers wheel reports as CSI. Quick edit and native mouse input
    /// would keep those reports in the console host.
    /// </summary>
    internal static uint ModeForReporting(uint mode)
    {
        var desired = mode | EnableVirtualTerminalInput | EnableExtendedFlags;
        desired &= ~EnableQuickEditMode;
        desired &= ~EnableMouseInput;
        return desired;
    }

    public static InputModeLease? EnableVirtualInput()
    {
        if (!TryReadInputMode(out var handle, out var mode))
        {
            return null;
        }

        if (!TryApplyReportingMode(handle, mode))
        {
            return null;
        }

        return new InputModeLease(handle, mode);
    }

    internal static void MaintainVirtualInput()
    {
        // Input mode only. The output code page stays with ConsoleTextEncoding,
        // and this does not change how ReadKey decodes keys or wheel reports.
        if (!TryReadInputMode(out var handle, out var mode))
        {
            return;
        }

        _ = TryApplyReportingMode(handle, mode);
    }

    private static bool TryReadInputMode(out nint handle, out uint mode)
    {
        handle = nint.Zero;
        mode = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        handle = GetStdHandle(StandardInputHandle);
        if (handle == nint.Zero || handle == unchecked((nint)(-1)))
        {
            return false;
        }

        return GetConsoleMode(handle, out mode);
    }

    private static bool TryApplyReportingMode(nint handle, uint mode)
    {
        var desired = ModeForReporting(mode);
        if (desired == mode)
        {
            return true;
        }

        return SetConsoleMode(handle, desired);
    }

    internal sealed class InputModeLease : IDisposable
    {
        private readonly nint _handle;
        private readonly uint _previousMode;
        private bool _restored;

        public InputModeLease(nint handle, uint previousMode)
        {
            _handle = handle;
            _previousMode = previousMode;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        public void Dispose()
        {
            if (_restored)
            {
                return;
            }

            if (!TryRestore())
            {
                throw new IOException("Could not restore the Windows console input mode.");
            }

            _restored = true;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }

        private void OnProcessExit(object? sender, EventArgs args) => _ = TryRestore();

        private bool TryRestore()
        {
            if (SetConsoleMode(_handle, _previousMode))
            {
                return true;
            }

            var currentHandle = GetStdHandle(StandardInputHandle);
            return currentHandle != nint.Zero
                && currentHandle != unchecked((nint)(-1))
                && GetConsoleMode(currentHandle, out var currentMode)
                && SetConsoleMode(currentHandle, currentMode & ~EnableVirtualTerminalInput);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
