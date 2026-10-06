using Spectre.Console;

namespace CrystalCode.Display.Shell;

/// <summary>
/// Alternate buffer for the session shell. Not AnsiConsole.Live.
/// Bracketed paste is on. Mouse reporting (1000 with SGR encoding 1006) is on
/// so the wheel scrolls the transcript by itself and Up/Down stay with input
/// history. The terminal's own text selection needs Shift (Option or Fn on
/// some macOS terminals) while the button is held.
/// </summary>
public sealed class AlternateScreen : IDisposable
{
    private const string ProductTitle = "Crystal Code";
    private bool _active;
    private bool _titlePushed;
    private string? _previousTitle;
    private WindowsConsole.InputModeLease? _inputMode;
    private EventHandler? _onProcessExit;
    private int _restored;

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
        var mouseReporting = false;
        AlternateScreen? screen = null;
        try
        {
            AnsiConsole.Write(new ControlCode("\u001b[?1049h"));
            entered = true;
            AnsiConsole.Write(new ControlCode("\u001b[?2004h"));
            bracketedPaste = true;
            AnsiConsole.Write(new ControlCode("\u001b[?1000h"));
            mouseReporting = true;
            AnsiConsole.Write(new ControlCode("\u001b[?1006h"));
            AnsiConsole.Write(new ControlCode("\u001b[H"));
            AnsiConsole.Write(new ControlCode("\u001b[2J"));
            screen = new AlternateScreen(true);
            screen._inputMode = inputMode;
            screen.ApplyWindowTitle();
            screen.ArmProcessExit();
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
                mouseReporting,
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
        bool mouseReporting,
        bool titlePushed)
    {
        var codes = new List<string>(5);
        if (titlePushed)
        {
            codes.Add("\u001b[23;0t");
        }

        if (mouseReporting)
        {
            codes.Add("\u001b[?1006l");
            codes.Add("\u001b[?1000l");
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

    /// <summary>
    /// Leaves the alternate buffer, then shows the cursor. Showing it first is
    /// discarded by terminals that restore the hidden cursor with the primary screen.
    /// </summary>
    internal static IReadOnlyList<string> LeaveSequences() =>
    [
        "\u001b[?1006l",
        "\u001b[?1000l",
        "\u001b[?2004l",
        "\u001b[?1049l",
        "\u001b[?25h"
    ];

    public void Dispose()
    {
        if (!_active)
        {
            return;
        }

        Restore(raw: false);
    }

    private void ArmProcessExit()
    {
        // Ctrl+C exits without unwinding the caller's using, so Dispose never runs.
        _onProcessExit = OnProcessExit;
        AppDomain.CurrentDomain.ProcessExit += _onProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        if (_active)
        {
            Restore(raw: true);
        }
    }

    private void DisarmProcessExit()
    {
        if (_onProcessExit is null)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit -= _onProcessExit;
        _onProcessExit = null;
    }

    private void Restore(bool raw)
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        DisarmProcessExit();
        try
        {
            if (_titlePushed)
            {
                WriteSequence("\u001b[23;0t", raw);
                _titlePushed = false;
            }

            RestoreConsoleTitle();
            foreach (var code in LeaveSequences())
            {
                WriteSequence(code, raw);
            }

            if (raw)
            {
                Console.Out.Flush();
            }
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

    private static void WriteSequence(string code, bool raw)
    {
        try
        {
            if (raw)
            {
                Console.Out.Write(code);
                return;
            }

            AnsiConsole.Write(new ControlCode(code));
        }
        catch (IOException)
        {
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
