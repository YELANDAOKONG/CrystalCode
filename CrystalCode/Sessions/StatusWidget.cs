using System.Globalization;

using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Approvals;
using CrystalCode.Display.Paint;

namespace CrystalCode.Sessions;

/// <summary>
/// Rich, width-aware status cards stored in the transcript.
/// </summary>
internal static class StatusWidget
{
    private const int ContextBarWidth = 16;

    public static IRenderable Create(SessionStatus status, bool full)
    {
        ArgumentNullException.ThrowIfNull(status);

        var layout = new Grid();
        layout.AddColumn(new GridColumn().PadRight(1));
        layout.AddColumn();
        layout.AddRow(WorkspaceCard(status), ModelCard(status));
        layout.AddRow(TokenCard(status, full), OptionsCard(status));
        if (full)
        {
            layout.AddRow(ActivityCard(status), ToolsCard(status));
        }

        return new Padder(
            new Rows(
                new Markup($"[{Theme.Heading}]Status{(full ? " · Full" : string.Empty)}[/]"),
                layout),
            new Padding(2, 0, 0, 0));
    }

    private static Panel WorkspaceCard(SessionStatus status) =>
        Card("Workspace",
        [
            ("Path", status.WorkspaceRoot),
            ("Mode", ModeLabel.For(status.PlanMode)),
            ("Approval", ApprovalLabel.For(status.Approval)),
            ("Prompt set", status.PromptSet)
        ]);

    private static Panel ModelCard(SessionStatus status) =>
        Card("Model",
        [
            ("Provider", status.Provider),
            ("Model", status.Model),
            ("Thinking", ThinkingValue(status.Thinking))
        ]);

    private static Panel TokenCard(SessionStatus status, bool full)
    {
        var blocks = new List<IRenderable>
        {
            FieldGrid(
            [
                ("Input", TokenValue(status.CumulativeUsage?.InputTokenCount)),
                ("Output", TokenValue(status.CumulativeUsage?.OutputTokenCount)),
                ("Total", TokenValue(status.CumulativeUsage?.TotalTokenCount))
            ]),
            SectionRule("Current context"),
            ContextGrid(status)
        };
        if (full)
        {
            blocks.Add(SectionRule("Latest request"));
            blocks.Add(
                FieldGrid(
                [
                    ("Input", TokenValue(status.Usage?.InputTokenCount)),
                    ("Output", TokenValue(status.Usage?.OutputTokenCount)),
                    ("Total", TokenValue(status.Usage?.TotalTokenCount))
                ]));
        }

        return Card("Tokens · Cumulative", new Rows(blocks));
    }

    private static Grid ContextGrid(SessionStatus status)
    {
        var grid = FieldGrid([("Window", Number(status.ContextWindow))]);
        grid.AddRow(
            new Markup($"[{Theme.Chrome}]Usage[/]"),
            ContextProgress(status));
        return grid;
    }

    private static Panel OptionsCard(SessionStatus status) =>
        Card("Options",
        [
            ("Skills", Toggle(status.SkillsEnabled)),
            ("External tools", Toggle(status.ExternalToolsEnabled)),
            ("Token estimate", Toggle(status.EstimatedTokensEnabled)),
            ("Verbose tools", Toggle(status.VerboseToolsEnabled)),
            ("Verbose commands", Toggle(status.VerboseCommandsEnabled))
        ]);

    private static Panel ActivityCard(SessionStatus status) =>
        Card("Activity",
        [
            ("Session ID", status.SessionId),
            ("Started", status.StartedUtc.ToUniversalTime().ToString(
                "yyyy-MM-dd HH:mm:ss 'UTC'",
                CultureInfo.InvariantCulture)),
            ("Turns", Number(status.UserTurns)),
            ("Model calls", Number(status.ModelCalls)),
            ("Tool calls", Number(status.ToolCalls)),
            ("Queued", Number(status.QueuedMessages)),
            ("Todos", Number(status.Todos))
        ]);

    private static Panel ToolsCard(SessionStatus status) =>
        Card("Tools",
        [
            ("Plan", Number(status.PlanTools)),
            ("Work", Number(status.WorkTools)),
            ("External", Number(status.ExternalTools))
        ]);

    private static Markup ContextProgress(SessionStatus status)
    {
        if (status.Usage is null)
        {
            return new Markup(
                $"[{Theme.Muted}]|{new string('-', ContextBarWidth)}|[/]  "
                + $"[{Theme.User}]--[/]");
        }

        var total = status.Usage.TotalTokenCount;
        var ratio = Math.Clamp((double)total / status.ContextWindow, 0, 1);
        var completed = (int)Math.Round(ratio * ContextBarWidth, MidpointRounding.AwayFromZero);
        var remaining = ContextBarWidth - completed;
        var percent = (int)(ratio * 100);
        return new Markup(
            $"[{Theme.Chrome}]|[/][{Theme.Accent}]{new string('#', completed)}[/]"
            + $"[{Theme.Muted}]{new string('-', remaining)}[/][{Theme.Chrome}]|[/]  "
            + $"[{Theme.User}]{percent}%[/]");
    }

    private static Panel Card(
        string title,
        IReadOnlyList<(string Field, string Value)> fields) =>
        Card(title, FieldGrid(fields));

    private static Panel Card(string title, IRenderable content) =>
        new(content)
        {
            Header = new PanelHeader($"[{Theme.Accent}]{MarkupText.Escape(title)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };

    private static Rule SectionRule(string title) =>
        new($"[{Theme.Chrome}]{MarkupText.Escape(title)}[/]")
        {
            Style = Style.Parse(Theme.Rule),
            Justification = Justify.Left
        };

    private static Grid FieldGrid(IReadOnlyList<(string Field, string Value)> fields)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        foreach (var (field, value) in fields)
        {
            grid.AddRow(
                new Markup($"[{Theme.Chrome}]{MarkupText.Escape(field)}[/]"),
                new Markup($"[{Theme.User}]{MarkupText.Escape(value)}[/]"));
        }

        return grid;
    }

    private static string ThinkingValue(string thinking)
    {
        const string prefix = "Think ";
        if (string.IsNullOrWhiteSpace(thinking))
        {
            return "Unavailable";
        }

        return thinking.StartsWith(prefix, StringComparison.Ordinal)
            ? thinking[prefix.Length..]
            : thinking;
    }

    private static string TokenValue(long? value) => value is null ? "--" : Number(value.Value);

    private static string Toggle(bool enabled) => enabled ? "On" : "Off";

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
