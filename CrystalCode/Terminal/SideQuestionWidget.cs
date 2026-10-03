using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Events;

namespace CrystalCode.Terminal;

internal static class SideQuestionWidget
{
    public static IRenderable Create(
        SideQuestionSnapshot snapshot,
        int index)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ReadSlot(snapshot, index, out var question, out var body, out var failure);
        var count = SlotCount(snapshot);
        var title = count > 1
            ? $"Side question  {index + 1}/{count}"
            : "Side question";
        var rows = new List<IRenderable>
        {
            new Markup($"[{Theme.Chrome}]{MarkupText.Escape(question)}[/]")
        };
        if (body.Length > 0)
        {
            rows.Add(Text.Empty);
            foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
            {
                rows.Add(line.Length == 0
                    ? Text.Empty
                    : new Markup($"[{Theme.User}]{MarkupText.Escape(line)}[/]"));
            }
        }

        if (!string.IsNullOrWhiteSpace(failure))
        {
            rows.Add(Text.Empty);
            rows.Add(new Markup($"[{Theme.Fail}]{MarkupText.Escape(failure)}[/]"));
        }

        rows.Add(Text.Empty);
        rows.Add(new Markup($"[{Theme.Muted}]Esc, Enter, or Space closes. Left and Right step. x clears.[/]"));
        return new Panel(new Rows(rows))
        {
            Header = new PanelHeader($"[{Theme.Heading}]{MarkupText.Escape(title)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
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
        out string? failure)
    {
        var count = SlotCount(snapshot);
        if (count == 0)
        {
            question = string.Empty;
            body = string.Empty;
            failure = snapshot.Failure;
            return;
        }

        index = Math.Clamp(index, 0, count - 1);
        if (!string.IsNullOrEmpty(snapshot.PendingQuestion) && index >= snapshot.Exchanges.Count)
        {
            question = snapshot.PendingQuestion;
            body = snapshot.Running && snapshot.LiveAnswer.Length == 0
                ? "Waiting for the model"
                : snapshot.LiveAnswer;
            failure = snapshot.Running ? null : snapshot.Failure;
            return;
        }

        var exchange = snapshot.Exchanges[index];
        question = exchange.Question;
        body = exchange.Answer;
        failure = null;
    }
}
