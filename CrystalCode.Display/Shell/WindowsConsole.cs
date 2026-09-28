using System.Runtime.InteropServices;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Turns on VT input on Windows so CSI sequences reach ReadKey.
/// ReadKey then reports Key empty for Tab/Enter; InputDecoder recovers them.
/// </summary>
internal static class WindowsConsole
{
    private const int StandardInputHandle = -10;
    private const uint EnableVirtualTerminalInput = 0x0200;

    public static InputModeLease? EnableVirtualInput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = GetStdHandle(StandardInputHandle);
        if (handle == nint.Zero || handle == unchecked((nint)(-1)))
        {
            return null;
        }

        if (!GetConsoleMode(handle, out var mode))
        {
            return null;
        }

        if ((mode & EnableVirtualTerminalInput) != 0)
        {
            return null;
        }

        if (!SetConsoleMode(handle, mode | EnableVirtualTerminalInput))
        {
            return null;
        }

        return new InputModeLease(handle, mode);
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
