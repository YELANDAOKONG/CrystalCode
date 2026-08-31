using Spectre.Console;
using Spectre.Console.Rendering;

using Crystal.Tools;

using CrystalHarness.Display.Paint;
using CrystalHarness.Sessions;

namespace CrystalHarness.Approvals;

/// <summary>
/// Permission card: Title Case fields inside a Spectre panel.
/// </summary>
public static class ApprovalCard
{
    public const string KeysHint = "Y Once  ·  S Session  ·  A Always  ·  N Deny";

    public static string ActionLine(ToolCall call) =>
        ToolCallText.Summary(call.Name, call.Arguments);

    public static string Field(string label, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(value);
        return label + "  " + DisplayCase.Token(value);
    }

    public static IReadOnlyList<string> PassLines(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        var lines = new List<string>
        {
            ActionLine(call),
            Field("Status", "allowed"),
            Field("Reason", reason.Value),
            Field("Risk", classification.Risk.Value),
            Field("Authority", classification.Authority.Value)
        };
        if (!string.IsNullOrWhiteSpace(classification.Summary))
        {
            lines.Add(classification.Summary);
        }

        if (review is not null)
        {
            lines.Add(Field("Outcome", review.Outcome));
            lines.Add(Field("Risk", review.RiskLevel.Value));
            lines.Add(Field("Authority", review.UserAuthorization.Value));
            foreach (var line in SplitRationale(review.Rationale))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public static IRenderable PassWidget(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        return Card(
            ActionLine(call),
            HostRows(classification, reason),
            classification.Summary,
            review,
            ApprovalDiff.Lines(call),
            footer: null,
            expand: false);
    }

    public static IRenderable AskWidget(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        return Card(
            ActionLine(call),
            AskRows(classification),
            classification.Summary,
            review,
            ApprovalDiff.Lines(call),
            KeysHint,
            expand: true);
    }

    public static IReadOnlyList<string> SplitRationale(string? rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var line in rationale.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }

    public static string CompactArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var flat = arguments
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (flat is "{}" or "")
        {
            return string.Empty;
        }

        return flat.Length <= 80 ? flat : flat[..77] + "...";
    }

    private static IRenderable Card(
        string header,
        IReadOnlyList<(string Label, string Value, string Color)> fields,
        string? summary,
        ApprovalReviewVerdict? review,
        IReadOnlyList<(string Color, string Text)> preview,
        string? footer,
        bool expand)
    {
        var blocks = new List<IRenderable> { FieldGrid(fields) };
        if (!string.IsNullOrWhiteSpace(summary))
        {
            blocks.Add(Prose(summary, Theme.Chrome));
        }

        if (review is not null)
        {
            blocks.Add(new Rule { Style = Style.Parse(Theme.Rule) });
            blocks.Add(
                FieldGrid(
                [
                    ("Outcome", DisplayCase.Token(review.Outcome), Theme.Ok),
                    ("Risk", DisplayCase.Token(review.RiskLevel.Value), Theme.Review),
                    ("Authority", DisplayCase.Token(review.UserAuthorization.Value), Theme.Review)
                ]));
            foreach (var line in SplitRationale(review.Rationale))
            {
                blocks.Add(Prose(line, Theme.Review));
            }
        }

        foreach (var line in preview)
        {
            blocks.Add(Prose(line.Text, line.Color));
        }

        if (!string.IsNullOrWhiteSpace(footer))
        {
            blocks.Add(new Markup($"[{Theme.Warning} bold]{MarkupText.Escape(footer)}[/]"));
        }

        var panel = new Panel(new Rows(blocks))
        {
            Header = new PanelHeader(MarkupText.Escape(header)),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = expand
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static List<(string Label, string Value, string Color)> HostRows(
        ToolClassification classification,
        ApprovalPassReason reason) =>
    [
        ("Status", DisplayCase.Token("allowed"), Theme.Ok),
        ("Reason", DisplayCase.Token(reason.Value), Theme.Review),
        ("Risk", DisplayCase.Token(classification.Risk.Value), Theme.Review),
        ("Authority", DisplayCase.Token(classification.Authority.Value), Theme.Review)
    ];

    private static List<(string Label, string Value, string Color)> AskRows(
        ToolClassification classification) =>
    [
        ("Risk", DisplayCase.Token(classification.Risk.Value), Theme.Review),
        ("Authority", DisplayCase.Token(classification.Authority.Value), Theme.Review)
    ];

    private static Grid FieldGrid(IReadOnlyList<(string Label, string Value, string Color)> fields)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        foreach (var field in fields)
        {
            grid.AddRow(
                new Markup($"[{Theme.Chrome}]{MarkupText.Escape(field.Label)}[/]"),
                new Markup($"[{field.Color}]{MarkupText.Escape(field.Value)}[/]"));
        }

        return grid;
    }

    private static Markup Prose(string text, string color) =>
        new($"[{color}]{MarkupText.Escape(text)}[/]");
}
