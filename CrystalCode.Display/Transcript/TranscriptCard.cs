using System.Text;
using CrystalCode.Display.Paint;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalCode.Display.Transcript;

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

        var content = RenderContent(kind, text);
        var borderStyle = BorderColor(kind);
        var panel = new Panel(content)
        {
            Header = new PanelHeader(header),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(borderStyle),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static IRenderable RenderContent(TranscriptKind kind, string text)
    {
        if (kind != TranscriptKind.Result)
        {
            return new Markup($"[{Color(kind)}]{MarkupText.Escape(text)}[/]");
        }

        var sb = new StringBuilder();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                sb.Append('[').Append(Theme.DiffAdded).Append(']').Append(MarkupText.Escape(line)).Append("[/]");
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                sb.Append('[').Append(Theme.DiffRemoved).Append(']').Append(MarkupText.Escape(line)).Append("[/]");
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                sb.Append('[').Append(Theme.Accent).Append(']').Append(MarkupText.Escape(line)).Append("[/]");
            }
            else if (line.StartsWith("... (", StringComparison.Ordinal))
            {
                sb.Append('[').Append(Theme.Muted).Append(']').Append(MarkupText.Escape(line)).Append("[/]");
            }
            else
            {
                sb.Append('[').Append(Theme.Ok).Append(']').Append(MarkupText.Escape(line)).Append("[/]");
            }

            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return new Markup(sb.ToString());
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

    private static string BorderColor(TranscriptKind kind) =>
        kind switch
        {
            TranscriptKind.User => Theme.Chrome,
            TranscriptKind.Thinking => Theme.Rule,
            TranscriptKind.Tool => Theme.Chrome,
            TranscriptKind.Result => Theme.Rule,
            TranscriptKind.Error => Theme.Fail,
            _ => Theme.Chrome
        };
}
