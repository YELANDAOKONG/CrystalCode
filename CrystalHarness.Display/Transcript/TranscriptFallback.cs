using Spectre.Console;

using CrystalHarness.Display.Paint;

namespace CrystalHarness.Display.Transcript;

/// <summary>
/// Sequential transcript writes when the alternate screen is not active.
/// </summary>
public static class TranscriptFallback
{
    public static void Write(TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var color = kind switch
        {
            TranscriptKind.Error => Theme.Fail,
            TranscriptKind.Result => Theme.Ok,
            TranscriptKind.Thinking => Theme.Thinking,
            TranscriptKind.Tool => Theme.Tool,
            TranscriptKind.Approval => Theme.Review,
            TranscriptKind.User => Theme.User,
            _ => Theme.Chrome
        };
        var card = TranscriptCard.TryCreate(kind, text);
        if (card is not null)
        {
            AnsiConsole.Write(card);
            AnsiConsole.WriteLine();
            return;
        }

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            AnsiConsole.MarkupLine($"[{color}]  {MarkupText.Escape(line)}[/]");
        }
    }

    public static void WriteDelta(TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var color = kind switch
        {
            TranscriptKind.Thinking => Theme.Thinking,
            TranscriptKind.Tool => Theme.Tool,
            _ => Theme.User
        };
        if (kind is TranscriptKind.Thinking or TranscriptKind.Tool)
        {
            AnsiConsole.Markup($"[{color}]{MarkupText.Escape(text)}[/]");
            return;
        }

        AnsiConsole.Markup(MarkupText.Escape(text));
    }
}
