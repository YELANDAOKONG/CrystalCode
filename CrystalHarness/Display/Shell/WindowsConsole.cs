using System.Runtime.InteropServices;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Turns on VT input on Windows so SGR wheel reports reach ReadKey.
/// </summary>
internal static class WindowsConsole
{
    private const int StandardInputHandle = -10;
    private const uint EnableVirtualTerminalInput = 0x0200;

    public static void EnableVirtualInput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetStdHandle(StandardInputHandle);
        if (handle == nint.Zero || handle == unchecked((nint)(-1)))
        {
            return;
        }

        if (!GetConsoleMode(handle, out var mode))
        {
            return;
        }

        _ = SetConsoleMode(handle, mode | EnableVirtualTerminalInput);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
