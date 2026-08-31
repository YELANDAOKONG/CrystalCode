using Spectre.Console;

namespace CrystalCode.Display.Paint;

public static class MarkupText
{
    public static string Escape(string? text) => Markup.Escape(text ?? string.Empty);
}
