using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Home;

namespace CrystalCode.Terminal;

/// <summary>
/// Chooses a theme inside the terminal shell. Moving the selection applies that
/// theme to the whole screen at once; Esc puts the saved theme back. Nothing is
/// written until Enter, which the session then saves.
/// </summary>
internal sealed class ThemePicker
{
    private const int MaximumVisibleRows = 8;
    private const int SampleRows = 10;
    private const int ChromeRows = 8;
    private readonly SessionRenderer _renderer;
    private readonly CrystalHome _home;

    public ThemePicker(SessionRenderer renderer, CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(home);
        _renderer = renderer;
        _home = home;
    }

    public async Task<string?> ChooseAsync(
        IReadOnlyList<string> names,
        string current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(current);
        if (names.Count == 0)
        {
            return null;
        }

        var selected = Math.Max(0, names.ToList().FindIndex(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase)));
        string? failure = null;
        _renderer.CloseStream();
        _renderer.PauseComposer();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                failure = ThemeApplier.TryApply(_home, names[selected], out var error) ? null : error;
                _renderer.SetOverlay(Render(names, selected, current, failure));
                _renderer.RepaintTheme();
                var key = await _renderer.ReadKeyAsync(scrollPlainArrows: false, cancellationToken);
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        Restore(current);
                        return null;
                    case ConsoleKey.Enter when failure is null:
                        return names[selected];
                    case ConsoleKey.UpArrow:
                        selected = Math.Max(0, selected - 1);
                        break;
                    case ConsoleKey.DownArrow:
                        selected = Math.Min(names.Count - 1, selected + 1);
                        break;
                    default:
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Restore(current);
            throw;
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
        }
    }

    private void Restore(string current)
    {
        // A saved theme file can be gone or broken by now. The screen then goes back to the
        // built-in palette instead of keeping a theme the operator only previewed.
        if (!ThemeApplier.TryApply(_home, current, out _))
        {
            Theme.Reset();
        }

        _renderer.RepaintTheme();
    }

    internal static IRenderable Render(IReadOnlyList<string> names, int selected, string current, string? failure)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[{Theme.Muted}]Up/Down preview  Enter use  Esc cancel[/]")
        };
        // The card also holds a hint, a rule, the sample, and its own frame.
        var visible = Math.Clamp(ScreenSize.Height - SampleRows - ChromeRows, 1, MaximumVisibleRows);
        visible = Math.Min(visible, names.Count);
        var start = Math.Clamp(selected - visible + 1, 0, Math.Max(0, names.Count - visible));
        var list = new List<IRenderable>();
        for (var index = start; index < start + visible; index++)
        {
            var name = names[index];
            var mark = index == selected ? "> " : "  ";
            var tag = string.Equals(name, current, StringComparison.OrdinalIgnoreCase) ? "  current" : string.Empty;
            var color = index == selected ? Theme.Selected : Theme.User;
            list.Add(new Markup($"[{color}]{MarkupText.Escape(mark + name + tag)}[/]"));
        }

        rows.Add(PickerScrollbar.Wrap(list, names.Count, start, names.Count > visible, entryRows: 1));
        rows.Add(new Rule { Style = Style.Parse(Theme.Rule) });
        if (failure is not null)
        {
            rows.Add(new Markup($"[{Theme.Fail}]{MarkupText.Escape(failure)}[/]"));
        }
        else
        {
            rows.AddRange(Sample());
        }

        var panel = new Panel(new Rows(rows))
        {
            Header = new PanelHeader("Theme"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    // A few of every kind of text the shell draws, so a theme can be judged without leaving the picker.
    private static IEnumerable<IRenderable> Sample()
    {
        yield return Line(Theme.Heading, "Heading");
        yield return Line(Theme.User, "User text");
        yield return Line(Theme.Chrome, "Chrome and borders");
        yield return Line(Theme.Muted, "Muted detail");
        yield return Line(Theme.Thinking, "Thinking text");
        yield return Line(Theme.Tool, "Tool call");
        yield return new Markup(
            $"[{Theme.Ok}]ok[/]  [{Theme.Warning}]warning[/]  [{Theme.Fail}]fail[/]  "
            + $"[{Theme.Work}]work[/]  [{Theme.Plan}]plan[/]  [{Theme.Custom}]custom[/]  [{Theme.Accent}]accent[/]");
        yield return new Markup($"[{Theme.Code} on {Theme.CodeBg}]  let total = count + 1;  [/]");
        foreach (var line in DiffLines.Hunk(["let count = 1;"], ["let total = 1;"]))
        {
            yield return new Markup(line.Markup);
        }
    }

    private static Markup Line(string color, string text) =>
        new($"[{color}]{MarkupText.Escape(text)}[/]");
}
