using Spectre.Console;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Alternate buffer for the session shell. Not AnsiConsole.Live.
/// Alternate-scroll turns the wheel into arrows. Bracketed paste is on.
/// Mouse tracking stays off so left-drag still selects text.
/// </summary>
public sealed class AlternateScreen : IDisposable
{
    private bool _active;

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

        try
        {
            WindowsConsole.EnableVirtualInput();
            AnsiConsole.Write(new ControlCode("\u001b[?1049h"));
            AnsiConsole.Write(new ControlCode("\u001b[?2004h"));
            AnsiConsole.Write(new ControlCode("\u001b[?1007h"));
            AnsiConsole.Write(new ControlCode("\u001b[H"));
            AnsiConsole.Write(new ControlCode("\u001b[2J"));
            return new AlternateScreen(true);
        }
        catch (IOException)
        {
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
            AnsiConsole.Cursor.Show();
            AnsiConsole.Write(new ControlCode("\u001b[?1007l"));
            AnsiConsole.Write(new ControlCode("\u001b[?2004l"));
            AnsiConsole.Write(new ControlCode("\u001b[?1049l"));
        }
        catch (IOException)
        {
        }

        _active = false;
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
