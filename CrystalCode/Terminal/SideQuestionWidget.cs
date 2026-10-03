using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Events;

namespace CrystalCode.Terminal;

internal static class SideQuestionWidget
{
    public const string WaitingCaption = "Waiting for the model";

    public static IRenderable Create(
        SideQuestionSnapshot snapshot,
        int index,
        int spinnerFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ReadSlot(snapshot, index, out var question, out var body, out var failure, out var waiting);
        var count = SlotCount(snapshot);
        var title = count > 1
            ? $"Side question  {index + 1}/{count}"
            : "Side question";
        var rows = new List<IRenderable>
        {
            TextLine(Theme.Chrome, question)
        };
        if (waiting)
        {
            rows.Add(Text.Empty);
            var glyph = ProgressSpinner.Frame(spinnerFrame);
            rows.Add(new Markup(
                $"[{Theme.Accent}]{MarkupText.Escape(glyph)}[/]  "
                + $"[{Theme.Muted}]{MarkupText.Escape(WaitingCaption)}[/]"));
        }
        else if (body.Length > 0)
        {
            rows.Add(Text.Empty);
            foreach (var line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                rows.Add(line.Length == 0
                    ? Text.Empty
                    : TextLine(Theme.User, line));
            }
        }

        if (!string.IsNullOrWhiteSpace(failure))
        {
            rows.Add(Text.Empty);
            rows.Add(TextLine(Theme.Fail, failure));
        }

        rows.Add(Text.Empty);
        rows.Add(new Markup($"[{Theme.Muted}]Esc, Enter, or Space closes. Left and Right step. x clears.[/]"));
        var panel = new Panel(new Rows(rows))
        {
            Header = new PanelHeader($"[{Theme.Heading}]{MarkupText.Escape(title)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    public static int SlotCount(SideQuestionSnapshot snapshot) =>
        snapshot.Exchanges.Count + (string.IsNullOrEmpty(snapshot.PendingQuestion) ? 0 : 1);

    public static int LatestSlot(SideQuestionSnapshot snapshot)
    {
        var count = SlotCount(snapshot);
        return count == 0 ? 0 : count - 1;
    }

    public static bool IsEmpty(SideQuestionSnapshot snapshot) =>
        snapshot.Exchanges.Count == 0
        && !snapshot.Running
        && string.IsNullOrEmpty(snapshot.PendingQuestion)
        && string.IsNullOrEmpty(snapshot.Failure);

    private static void ReadSlot(
        SideQuestionSnapshot snapshot,
        int index,
        out string question,
        out string body,
        out string? failure,
        out bool waiting)
    {
        var count = SlotCount(snapshot);
        if (count == 0)
        {
            question = string.Empty;
            body = string.Empty;
            failure = snapshot.Failure;
            waiting = false;
            return;
        }

        index = Math.Clamp(index, 0, count - 1);
        if (!string.IsNullOrEmpty(snapshot.PendingQuestion) && index >= snapshot.Exchanges.Count)
        {
            question = snapshot.PendingQuestion;
            waiting = snapshot.Running && snapshot.LiveAnswer.Length == 0;
            body = waiting ? string.Empty : snapshot.LiveAnswer;
            failure = snapshot.Running ? null : snapshot.Failure;
            return;
        }

        var exchange = snapshot.Exchanges[index];
        question = exchange.Question;
        body = exchange.Answer;
        failure = null;
        waiting = false;
    }

    private static Markup TextLine(string color, string text) =>
        new($"[{color}]{MarkupText.Escape(TextWidth.ExpandTabs(text))}[/]");
}
