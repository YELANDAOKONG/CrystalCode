using Spectre.Console;

namespace CrystalHarness.Display.Paint;

public static class MarkupText
{
    public static string Escape(string? text) => Markup.Escape(text ?? string.Empty);
}
