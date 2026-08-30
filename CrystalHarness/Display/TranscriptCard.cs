using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalHarness.Display;

/// <summary>
/// Shared rounded panel for user, thinking, tool, and result blocks.
/// </summary>
public static class TranscriptCard
{
    public static IRenderable? TryCreate(TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return null;
        }

        var header = Header(kind);
        if (header is null)
        {
            return null;
        }

        var panel = new Panel(new Markup($"[{Color(kind)}]{MarkupText.Escape(text)}[/]"))
        {
            Header = new PanelHeader(header),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static string? Header(TranscriptKind kind) =>
        kind switch
        {
            TranscriptKind.User => "You",
            TranscriptKind.Thinking => "Thinking",
            TranscriptKind.Tool => "Tool",
            TranscriptKind.Result => "Result",
            TranscriptKind.Error => "Error",
            _ => null
        };

    private static string Color(TranscriptKind kind) =>
        kind switch
        {
            TranscriptKind.User => Theme.User,
            TranscriptKind.Thinking => Theme.Thinking,
            TranscriptKind.Tool => Theme.Tool,
            TranscriptKind.Result => Theme.Ok,
            TranscriptKind.Error => Theme.Fail,
            _ => Theme.Chrome
        };
}
