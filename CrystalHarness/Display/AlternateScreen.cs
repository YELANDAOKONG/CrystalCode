using Spectre.Console;

namespace CrystalHarness.Display;

/// <summary>
/// Alternate buffer for the session shell. Not AnsiConsole.Live.
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
            AnsiConsole.Write(new ControlCode("\u001b[?1049h"));
            AnsiConsole.Write(new ControlCode("\u001b[?1000h"));
            AnsiConsole.Write(new ControlCode("\u001b[?1006h"));
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
            AnsiConsole.Write(new ControlCode("\u001b[?1006l"));
            AnsiConsole.Write(new ControlCode("\u001b[?1000l"));
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
