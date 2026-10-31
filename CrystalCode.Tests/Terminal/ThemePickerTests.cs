using CrystalCode.Display.Paint;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ThemePickerTests
{
    [Fact]
    public void Render_ListsNamesMarksTheSelectionAndCurrentAndShowsASample()
    {
        var text = Draw(ThemePicker.Render(["default", "dark", "light", "night"], 2, "dark", failure: null));

        Assert.Contains("  default", text, StringComparison.Ordinal);
        Assert.Contains("  dark  current", text, StringComparison.Ordinal);
        Assert.Contains("> light", text, StringComparison.Ordinal);
        Assert.Contains("Heading", text, StringComparison.Ordinal);
        Assert.Contains("-let count = 1;", text, StringComparison.Ordinal);
        Assert.Contains("+let total = 1;", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\u2503', text);
    }

    [Fact]
    public void Render_ShowsTheFailureInsteadOfTheSample()
    {
        var text = Draw(ThemePicker.Render(["default", "broken"], 1, "default", "Invalid theme color  accent"));

        Assert.Contains("Invalid theme color  accent", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Heading", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DrawsAScrollbarWhenThereAreMoreThemesThanRows()
    {
        var names = Enumerable.Range(0, 40).Select(index => "theme" + index).ToArray();

        var text = Draw(ThemePicker.Render(names, 20, "theme0", failure: null));

        Assert.Contains('\u2503', text);
    }

    private static string Draw(Spectre.Console.Rendering.IRenderable widget)
    {
        var writer = new StringWriter();
        var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
        {
            Ansi = Spectre.Console.AnsiSupport.No,
            ColorSystem = Spectre.Console.ColorSystemSupport.NoColors,
            Out = new Spectre.Console.AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 100;
        console.Write(widget);
        return writer.ToString();
    }
}
