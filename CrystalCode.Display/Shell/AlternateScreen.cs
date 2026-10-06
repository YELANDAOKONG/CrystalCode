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
    private bool _titlePushed;
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
        var entered = false;
        var bracketedPaste = false;
        var alternateScroll = false;
        AlternateScreen? screen = null;
        try
        {
            AnsiConsole.Write(new ControlCode("\u001b[?1049h"));
            entered = true;
            AnsiConsole.Write(new ControlCode("\u001b[?2004h"));
            bracketedPaste = true;
            AnsiConsole.Write(new ControlCode("\u001b[?1007h"));
            alternateScroll = true;
            AnsiConsole.Write(new ControlCode("\u001b[H"));
            AnsiConsole.Write(new ControlCode("\u001b[2J"));
            screen = new AlternateScreen(true);
            screen._inputMode = inputMode;
            screen.ApplyWindowTitle();
            return screen;
        }
        catch (IOException)
        {
            if (screen is not null)
            {
                screen._inputMode = null;
            }

            foreach (var code in RecoverySequences(
                entered,
                bracketedPaste,
                alternateScroll,
                screen is not null && screen._titlePushed))
            {
                try
                {
                    AnsiConsole.Write(new ControlCode(code));
                }
                catch (IOException)
                {
                }
            }

            screen?.RestoreConsoleTitle();
            inputMode?.Dispose();
            return new AlternateScreen(false);
        }
    }

    /// <summary>
    /// Undo setup that already succeeded. Modes that were not enabled stay as they were.
    /// </summary>
    internal static IReadOnlyList<string> RecoverySequences(
        bool alternateBuffer,
        bool bracketedPaste,
        bool alternateScroll,
        bool titlePushed)
    {
        var codes = new List<string>(4);
        if (titlePushed)
        {
            codes.Add("\u001b[23;0t");
        }

        if (alternateScroll)
        {
            codes.Add("\u001b[?1007l");
        }

        if (bracketedPaste)
        {
            codes.Add("\u001b[?2004l");
        }

        if (alternateBuffer)
        {
            codes.Add("\u001b[?1049l");
        }

        return codes;
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
        _titlePushed = true;
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
        if (_titlePushed)
        {
            AnsiConsole.Write(new ControlCode("\u001b[23;0t"));
            _titlePushed = false;
        }

        RestoreConsoleTitle();
    }

    private void RestoreConsoleTitle()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(_previousTitle))
        {
            return;
        }

        try
        {
            Console.Title = _previousTitle;
        }
        catch (IOException)
        {
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
