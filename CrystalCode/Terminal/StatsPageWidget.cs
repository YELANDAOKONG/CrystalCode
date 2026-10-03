using System.Globalization;

using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Terminal;

internal static class StatsPageWidget
{
    private const int PageWidth = 56;

    private const int InnerWidth = 52;

    private const int BarWidth = 16;

    private const int NameWidth = 16;

    public static IRenderable Create(SessionStatsReport report, bool allWorkspaces)
    {
        ArgumentNullException.ThrowIfNull(report);
        var scope = allWorkspaces ? "All workspaces" : "This workspace";
        return new Align(
            new Rows(
                new Markup($"[{Theme.Heading}]Stats[/]"),
                new Markup($"[{Theme.Muted}]{scope}[/]"),
                Text.Empty,
                Section("Overview", Fields(OverviewRows(report))),
                Text.Empty,
                Section("Tokens", Fields(TokenRows(report))),
                Text.Empty,
                Section("Tools", ToolBody(report)),
                Text.Empty,
                new Markup($"[{Theme.Muted}]Esc or q to close[/]")),
            HorizontalAlignment.Center);
    }

    private static IRenderable Section(string title, IRenderable body) =>
        new Panel(body)
        {
            Header = new PanelHeader($"[{Theme.Heading}] {MarkupText.Escape(title)} [/]", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Width = PageWidth,
            Expand = false
        };

    private static IReadOnlyList<(string Label, string Value)> OverviewRows(SessionStatsReport report) =>
    [
        ("Sessions", Count(report.Sessions)),
        ("User turns", Count(report.UserTurns)),
        ("Model calls", Count(report.ModelCalls)),
        ("Tool calls", Count(report.ToolCalls)),
        ("Days", Count(report.WindowDays)),
        ("Earliest", When(report.Earliest)),
        ("Latest", When(report.Latest))
    ];

    private static IReadOnlyList<(string Label, string Value)> TokenRows(SessionStatsReport report) =>
    [
        ("Input", Count(report.InputTokens)),
        ("Output", Count(report.OutputTokens)),
        ("Reasoning", Count(report.ReasoningTokens)),
        ("Total", Count(report.TotalTokens)),
        ("Average / session", Count(report.AverageTokensPerSession)),
        ("Median / session", Count(report.MedianTokensPerSession))
    ];

    private static Grid Fields(IReadOnlyList<(string Label, string Value)> rows)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn { Width = 24, NoWrap = true, Padding = new Padding(0, 0, 2, 0) });
        grid.AddColumn(new GridColumn
        {
            Width = InnerWidth - 24,
            NoWrap = true,
            Alignment = Justify.Right,
            Padding = new Padding(0)
        });
        foreach (var row in rows)
        {
            grid.AddRow(
                new Markup($"[{Theme.Chrome}]{MarkupText.Escape(row.Label)}[/]"),
                new Markup($"[{Theme.User}]{MarkupText.Escape(row.Value)}[/]"));
        }

        return grid;
    }

    private static IRenderable ToolBody(SessionStatsReport report)
    {
        if (report.TopTools.Count == 0)
        {
            return new Markup($"[{Theme.Muted}]None[/]");
        }

        var max = 1;
        foreach (var tool in report.TopTools)
        {
            max = Math.Max(max, tool.Count);
        }

        var grid = new Grid();
        // A column wraps once its text fills the requested width, so the bar cell is one wider.
        grid.AddColumn(new GridColumn { Width = NameWidth + 1, NoWrap = true, Padding = new Padding(0, 0, 1, 0) });
        grid.AddColumn(new GridColumn { Width = BarWidth + 1, NoWrap = true, Padding = new Padding(0) });
        grid.AddColumn(new GridColumn
        {
            Width = InnerWidth - NameWidth - BarWidth - 2,
            NoWrap = true,
            Alignment = Justify.Right,
            Padding = new Padding(0)
        });
        foreach (var tool in report.TopTools)
        {
            grid.AddRow(
                new Markup($"[{Theme.Tool}]{MarkupText.Escape(FitName(tool.Name))}[/]"),
                Bar(tool.Count, max),
                new Markup(
                    $"[{Theme.User}]{MarkupText.Escape(Count(tool.Count))}[/] "
                    + $"[{Theme.Muted}]{MarkupText.Escape(Share(tool.Count, report.ToolCalls))}[/]"));
        }

        return grid;
    }

    private static Markup Bar(int count, int max)
    {
        var filled = max <= 0 || count <= 0
            ? 0
            : Math.Clamp((int)Math.Round(count / (double)max * BarWidth), 1, BarWidth);
        var fill = new string('█', filled);
        var track = new string('░', BarWidth - filled);
        return new Markup($"[{Theme.Accent}]{fill}[/][{Theme.Muted}]{track}[/]");
    }

    private static string FitName(string name)
    {
        if (name.Length <= NameWidth)
        {
            return name;
        }

        return string.Concat(name.AsSpan(0, NameWidth - 2), "..");
    }

    private static string Share(int count, int total)
    {
        var percent = total <= 0 ? 0 : count * 100d / total;
        return percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string When(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "--";
}
