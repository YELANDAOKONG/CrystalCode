using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Home;

namespace CrystalCode.Sessions;

/// <summary>Chooses a saved session in update order inside the terminal shell.</summary>
internal sealed class SessionPicker
{
    private const int MaximumVisibleRows = 8;
    private readonly SessionRenderer _renderer;

    public SessionPicker(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public async Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count == 0)
        {
            return null;
        }

        var query = string.Empty;
        var selected = 0;
        _renderer.CloseStream();
        _renderer.PauseComposer();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = Filter(sessions, query);
                selected = Math.Clamp(selected, 0, Math.Max(0, matches.Count - 1));
                _renderer.SetOverlay(Render(matches, query, selected, currentId));
                var key = await _renderer.ReadKeyAsync(
                    scrollPlainArrows: false,
                    cancellationToken);

                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        return null;
                    case ConsoleKey.Enter when matches.Count > 0:
                        return matches[selected].Id;
                    case ConsoleKey.UpArrow:
                        selected = Math.Max(0, selected - 1);
                        break;
                    case ConsoleKey.DownArrow:
                        selected = Math.Min(matches.Count - 1, selected + 1);
                        break;
                    case ConsoleKey.Backspace when query.Length > 0:
                        query = query[..^1];
                        selected = 0;
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            query += key.KeyChar;
                            selected = 0;
                        }

                        break;
                }
            }
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
        }
    }

    internal static IReadOnlyList<SessionSummary> Filter(
        IReadOnlyList<SessionSummary> sessions,
        string query)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return sessions;
        }

        return sessions.Where(session =>
            session.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || session.Preview.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IRenderable Render(
        IReadOnlyList<SessionSummary> matches,
        string query,
        int selected,
        string? currentId)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[{Theme.Muted}]Newest first  Type to filter  Enter resume  Esc cancel[/]"),
            new Markup($"[{Theme.Accent}]Search > [/]{MarkupText.Escape(query)}")
        };
        if (matches.Count == 0)
        {
            rows.Add(new Markup($"[{Theme.Muted}]No matching sessions[/]"));
        }
        else
        {
            var visibleRows = VisibleRows();
            var start = Math.Clamp(
                selected - visibleRows + 1,
                0,
                Math.Max(0, matches.Count - visibleRows));
            var end = Math.Min(matches.Count, start + visibleRows);
            for (var index = start; index < end; index++)
            {
                var session = matches[index];
                var time = session.UpdatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    ?? "Unknown time";
                var current = string.Equals(session.Id, currentId, StringComparison.Ordinal)
                    ? "  current"
                    : string.Empty;
                var preview = TextWidth.Truncate(
                    session.Preview,
                    Math.Max(8, ScreenSize.Width - 48));
                var label = $"{time}  {session.UserTurns} turns{current}  {preview}";
                var color = index == selected ? Theme.Selected : Theme.User;
                rows.Add(new Markup($"[{color}]{(index == selected ? "> " : "  ")}{MarkupText.Escape(label)}[/]"));
                rows.Add(new Markup($"[{Theme.Muted}]  {MarkupText.Escape(session.Id)}[/]"));
            }
        }

        var panel = new Panel(new Rows(rows))
        {
            Header = new PanelHeader("Resume Session"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static int VisibleRows() =>
        Math.Min(MaximumVisibleRows, Math.Max(1, (ScreenSize.Height - 10) / 2));
}
