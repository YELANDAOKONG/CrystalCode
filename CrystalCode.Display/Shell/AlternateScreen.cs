using Spectre.Console;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Alternate buffer for the session shell. Not AnsiConsole.Live.
/// Alternate scroll turns the wheel into arrows. Bracketed paste is on.
/// Mouse tracking stays off so left-drag still selects text.
/// </summary>
public sealed class AlternateScreen : IDisposable
{
    private const string ProductTitle = "Crystal Code";
    private bool _active;
    private string? _previousTitle;
    private WindowsConsole.InputModeLease? _inputMode;

    private AlternateScreen(bool active)
    {
        _active = active;
    }

    public bool IsActive => _active;

    public static AlternateScreen TryEnter()
    {
        if (!IsSupported())
        {
            return new AlternateScreen(false);
        }

        var inputMode = WindowsConsole.EnableVirtualInput();
        try
        {
            AnsiConsole.Write(new ControlCode("\u001b[?1049h"));
            AnsiConsole.Write(new ControlCode("\u001b[?2004h"));
            AnsiConsole.Write(new ControlCode("\u001b[?1007h"));
            AnsiConsole.Write(new ControlCode("\u001b[H"));
            AnsiConsole.Write(new ControlCode("\u001b[2J"));
            var screen = new AlternateScreen(true);
            screen._inputMode = inputMode;
            screen.ApplyWindowTitle();
            return screen;
        }
        catch (IOException)
        {
            inputMode?.Dispose();
            return new AlternateScreen(false);
        }
    }

    public void Dispose()
    {
        if (!_active)
        {
            return;
        }

        try
        {
            RestoreWindowTitle();
            AnsiConsole.Cursor.Show();
            AnsiConsole.Write(new ControlCode("\u001b[?1007l"));
            AnsiConsole.Write(new ControlCode("\u001b[?2004l"));
            AnsiConsole.Write(new ControlCode("\u001b[?1049l"));
        }
        catch (IOException)
        {
        }
        finally
        {
            var inputMode = _inputMode;
            _inputMode = null;
            _active = false;
            inputMode?.Dispose();
        }
    }

    private void ApplyWindowTitle()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _previousTitle = Console.Title;
            }
            catch (IOException)
            {
            }
        }

        AnsiConsole.Write(new ControlCode("\u001b[22;0t"));
        AnsiConsole.Write(new ControlCode($"\u001b]0;{ProductTitle}\u0007"));
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Console.Title = ProductTitle;
            }
            catch (IOException)
            {
            }
        }
    }

    private void RestoreWindowTitle()
    {
        AnsiConsole.Write(new ControlCode("\u001b[23;0t"));
        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(_previousTitle))
        {
            try
            {
                Console.Title = _previousTitle;
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool IsSupported()
    {
        try
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                return false;
            }

            var capabilities = AnsiConsole.Profile.Capabilities;
            return capabilities.Ansi && capabilities.Interactive;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
