using System.Globalization;

using CrystalCode.Display.Input;
using CrystalCode.Display.Input.Interfaces;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions.Pursue;

namespace CrystalCode.Terminal;

/// <summary>A live inspector with stable check selection and a separately scrollable evidence pane.</summary>
internal sealed class PursuePage
{
    private const int WideColumns = 100;
    private const int HeaderRows = 5;
    private const int FooterRows = 2;
    private const int SeparatorColumns = 3;
    private const int MaximumStackedChecks = 4;
    private const int BarColumns = 10;
    private PursueState? _state;
    private string? _selectedId;
    private bool _followLatest = true;
    private int _scroll;
    private int _detailRows = 1;
    private int _detailCount;
    private string? _cachedId;
    private PursueState? _cachedState;
    private int _cachedWidth;
    private IReadOnlyList<(string Text, string Style)> _detail = [];
    private string? _notice;

    public void ShowError(string text) => _notice = text;

    internal string? SelectedId => _selectedId;
    internal int Scroll => _scroll;
    internal bool FollowLatest => _followLatest;

    public void Update(PursueState? state)
    {
        if (_state?.Id != state?.Id)
        {
            _selectedId = null;
            _followLatest = true;
            _scroll = 0;
            _notice = null;
        }

        _state = state;
        if (_followLatest || !History.Any(check => check.Id == _selectedId))
        {
            var latest = History.LastOrDefault()?.Id;
            if (latest != _selectedId)
            {
                _scroll = 0;
            }

            _selectedId = latest;
        }
    }

    public bool Closed { get; private set; }

    public string? Handle(IInputEvent input)
    {
        switch (input)
        {
            case InputKey { Key: ConsoleKey.Escape or ConsoleKey.Q or ConsoleKey.F9, Modifiers: ConsoleModifiers.None }:
                Closed = true;
                break;
            case InputKey { Key: ConsoleKey.UpArrow, Modifiers: ConsoleModifiers.None }:
                Select(1);
                break;
            case InputKey { Key: ConsoleKey.DownArrow, Modifiers: ConsoleModifiers.None }:
                Select(-1);
                break;
            case InputKey { Key: ConsoleKey.L, Modifiers: ConsoleModifiers.None }:
                _followLatest = true;
                _selectedId = History.LastOrDefault()?.Id;
                _scroll = 0;
                break;
            case InputKey { Key: ConsoleKey.Home }:
                _scroll = 0;
                break;
            case InputKey { Key: ConsoleKey.End }:
                _scroll = Math.Max(0, _detailCount - _detailRows);
                break;
            case InputKey { Key: ConsoleKey.PageUp }:
                _scroll = Math.Max(0, _scroll - Math.Max(1, _detailRows - 1));
                break;
            case InputKey { Key: ConsoleKey.PageDown }:
                _scroll = Math.Min(Math.Max(0, _detailCount - _detailRows), _scroll + Math.Max(1, _detailRows - 1));
                break;
            case InputWheel wheel:
                _scroll = Math.Clamp(_scroll - wheel.Delta, 0, Math.Max(0, _detailCount - _detailRows));
                break;
            case InputKey { Key: ConsoleKey.P, Modifiers: ConsoleModifiers.None } when _state?.Status == PursueStatus.Active:
                _notice = null;
                return "/pursue pause";
            case InputKey { Key: ConsoleKey.R, Modifiers: ConsoleModifiers.None } when _state?.Status is PursueStatus.Paused or PursueStatus.Blocked:
                _notice = null;
                return "/pursue resume";
            default:
                break;
        }

        return null;
    }

    public IReadOnlyList<PaintLine> Paint(int width, int height, PursueConfiguration? configuration, PaintLine progress)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bodyRows = Math.Max(1, height - HeaderRows - FooterRows);
        var frame = new List<PaintLine>
        {
            Line(Theme.Heading, "Pursue" + (configuration is null ? string.Empty
                : "   Checker  " + (configuration.Model.UsesSessionModel ? "Session model"
                    : configuration.Model.Provider + "/" + configuration.Model.Model)), width),
            Line(StatusStyle(), Status(), width),
            Line(Theme.User, "Objective  " + (_state?.Objective ?? "No objective. Use /pursue start <objective> in the composer."), width),
            Budget(configuration, width),
            Line(Theme.Rule, new string('─', width), width)
        };
        frame.AddRange(Body(width, bodyRows));
        frame.Add(_notice is not null ? Line(Theme.Fail, _notice, width)
            : progress.Plain.Length > 0 ? progress.Fit(width) : Line(Theme.Muted, IdleHint(), width));
        var actions = _state?.Status switch
        {
            PursueStatus.Active => "  P pause",
            PursueStatus.Paused or PursueStatus.Blocked => "  R resume",
            _ => string.Empty
        };
        frame.Add(Line(Theme.Chrome, width < WideColumns
            ? "Esc/Q/F9 back  Up/Down checks  PgUp/PgDn read  L latest" + actions
            : "Esc/Q/F9 back  Up/Down checks  PgUp/PgDn or wheel read  Home/End  L latest" + actions, width));
        return Enumerable.Range(0, height).Select(row => row < frame.Count ? frame[row].Fit(width) : PaintLine.Blank).ToArray();
    }

    private IReadOnlyList<PursueCheckRecord> History => _state?.History ?? [];

    private void Select(int delta)
    {
        if (History.Count == 0)
        {
            return;
        }

        var index = SelectedIndex();
        _selectedId = History[Math.Clamp(index + delta, 0, History.Count - 1)].Id;
        _followLatest = false;
        _scroll = 0;
    }

    private int SelectedIndex()
    {
        for (var index = 0; index < History.Count; index++)
        {
            if (History[index].Id == _selectedId)
            {
                return index;
            }
        }

        return Math.Max(0, History.Count - 1);
    }

    private IReadOnlyList<PaintLine> Body(int width, int rows)
    {
        var wide = width >= WideColumns;
        var listWidth = wide ? Math.Clamp(width / 3, 28, 42) : width;
        var detailWidth = wide ? width - listWidth - SeparatorColumns : width;
        var stackedRows = Math.Min(MaximumStackedChecks, Math.Max(1, (rows - 2) / 3));
        var listRows = wide ? rows - 1 : Math.Min(Math.Max(1, History.Count), stackedRows);
        var detailRows = wide ? rows - 1 : Math.Max(1, rows - listRows - 2);
        listRows = Math.Max(1, listRows);
        _detailRows = Math.Max(1, detailRows);
        var detail = Detail(Math.Max(1, detailWidth - TranscriptScrollbar.Columns));
        _detailCount = detail.Count;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, detail.Count - _detailRows));
        var selected = SelectedIndex();
        // History is saved oldest first; the inspector lists the newest check first.
        var selectedRow = Math.Max(0, History.Count - 1 - selected);
        var start = Math.Clamp(selectedRow - listRows + 1, 0, Math.Max(0, History.Count - listRows));
        var list = new List<PaintLine>();
        for (var row = 0; row < listRows; row++)
        {
            var index = History.Count - 1 - start - row;
            var content = index >= 0 ? CheckRow(History[index], index == selected, listWidth - TranscriptScrollbar.Columns)
                : index == -1 && History.Count == 0 ? Line(Theme.Muted, "No checks yet", listWidth - TranscriptScrollbar.Columns) : PaintLine.Blank;
            list.Add(Column(content, listWidth, TranscriptScrollbar.Cell(row,
                TranscriptScrollbar.ThumbFromTop(History.Count, listRows, start))));
        }

        var preview = new List<PaintLine>();
        var thumb = TranscriptScrollbar.ThumbFromTop(detail.Count, _detailRows, _scroll);
        for (var row = 0; row < _detailRows; row++)
        {
            var index = _scroll + row;
            var content = index < detail.Count ? Line(Color(detail[index].Style), detail[index].Text, detailWidth - TranscriptScrollbar.Columns) : PaintLine.Blank;
            preview.Add(Column(content, detailWidth, TranscriptScrollbar.Cell(row, thumb)));
        }

        var listTitle = $"Checks {History.Count:N0}" + (_followLatest ? "  Following latest" : "  Selection held");
        var detailTitle = $"Details  {_scroll + 1}-{Math.Min(detail.Count, _scroll + _detailRows)}/{detail.Count}";
        var result = new List<PaintLine>();
        if (wide)
        {
            var separator = PaintLine.Colored(Theme.Rule, " │ ");
            result.Add(Join(Pad(Line(Theme.Chrome, listTitle, listWidth), listWidth), separator, Line(Theme.Chrome, detailTitle, detailWidth)));
            for (var row = 0; row < listRows; row++)
            {
                result.Add(Join(list[row], separator, preview[row]));
            }
        }
        else
        {
            result.Add(Line(Theme.Chrome, listTitle, width));
            result.AddRange(list);
            result.Add(Line(Theme.Chrome, detailTitle, width));
            result.AddRange(preview);
        }

        return result.Take(rows).ToArray();
    }

    private IReadOnlyList<(string Text, string Style)> Detail(int width)
    {
        var check = History.FirstOrDefault(item => item.Id == _selectedId);
        if (_cachedId == check?.Id && _cachedState == _state && _cachedWidth == width)
        {
            return _detail;
        }

        var lines = new List<(string Text, string Style)>();
        if (check is null)
        {
            Add("Objective", "heading");
            Add(_state?.Objective ?? "No objective.", "user");
            if (_state?.Reason is { Length: > 0 } reason)
            {
                Add("Reason", "heading");
                Add(reason, "user");
            }

            if (_state?.NextStep is { Length: > 0 } next)
            {
                Add("Next step", "heading");
                Add(next, "user");
            }

            Add(_state is null ? "Close this page and use /pursue start <objective> to begin."
                : "Checks appear after the working model finishes a segment. Older saved sessions may have no recorded checks.", "muted");
        }
        else
        {
            Add($"Check {check.Index}  {DisplayCase.Token(check.Verdict)}", "heading");
            Add(check.CheckedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
                + "  Run " + check.RunId[..Math.Min(8, check.RunId.Length)], "muted");
            Add("Reason", "heading");
            Add(check.Reason, "user");
            if (check.NextStep.Length > 0)
            {
                Add("Next step", "heading");
                Add(check.NextStep, "user");
            }

            if (check.WaitFor.Count > 0)
            {
                Add("Waiting for tasks", "heading");
                Add(string.Join(", ", check.WaitFor), "user");
            }

            if (check.Usage is { } usage)
            {
                Add($"Tokens  {usage.InputTokenCount:N0} in / {usage.OutputTokenCount:N0} out", "muted");
            }

            Add(string.Empty, "user");
            Add($"Evidence ({check.Evidence.Count})", "heading");
            if (check.Evidence.Count == 0)
            {
                Add("No evidence was cited by this check.", "muted");
            }

            foreach (var entry in check.Evidence)
            {
                Add($"[{entry.Number}] {DisplayCase.Token(entry.Kind)}", "heading");
                Add(entry.Text, "user");
                Add(string.Empty, "user");
            }

            Add("Objective", "heading");
            Add(_state?.Objective ?? "No objective.", "user");
            Add("Run " + check.RunId, "muted");
            if (_state?.Reason is { Length: > 0 } reason)
            {
                Add("Current stop reason", "heading");
                Add(reason, "user");
            }
        }

        _cachedId = check?.Id;
        _cachedState = _state;
        _cachedWidth = width;
        _detail = lines;
        return lines;

        void Add(string text, string style)
        {
            foreach (var line in TextWidth.Wrap(TextWidth.ExpandTabs(TerminalText.Sanitize(text)), width))
            {
                lines.Add((line, style));
            }
        }
    }

    private PaintLine CheckRow(PursueCheckRecord check, bool selected, int width)
    {
        var mark = selected ? "> " : "  ";
        return Line(selected ? Theme.Selected : VerdictStyle(check.Verdict),
            $"{mark}#{check.Index} {DisplayCase.Token(check.Verdict)}  {check.CheckedUtc.ToLocalTime():HH:mm:ss}  {check.Reason}", width);
    }

    private PaintLine Budget(PursueConfiguration? configuration, int width)
    {
        if (configuration is null || _state is null)
        {
            return Line(Theme.Muted, "Budget use appears when an objective is running. F9 opens this page.", width);
        }

        var limits = configuration.Limits;
        var columns = Math.Max(1, (width - SeparatorColumns) / 2);
        var left = Gauge("Continuations", _state.Continuations, limits.MaximumContinuations, columns);
        var right = Gauge("Stalls", _state.Stalls, limits.MaximumStalls, columns);
        var separator = PaintLine.Colored(Theme.Rule, "   ");
        return Join(left, separator, right).Fit(width);
    }

    private static PaintLine Gauge(string label, int value, int? maximum, int columns)
    {
        var count = value.ToString("N0", CultureInfo.InvariantCulture);
        if (maximum is null)
        {
            return PaintLine.Colored(Theme.Chrome, $"{label} {count} / unlimited");
        }

        var caption = $"{label} {count}/{maximum.Value.ToString("N0", CultureInfo.InvariantCulture)} ";
        var barWidth = Math.Clamp(columns - caption.Length, 0, BarColumns);
        var filled = maximum == 0 ? 0 : Math.Clamp((int)Math.Round((double)value / maximum.Value * barWidth), 0, barWidth);
        var color = value >= maximum ? Theme.Warning : Theme.Accent;
        var fill = new string('━', filled);
        var rest = new string('─', barWidth - filled);
        return new PaintLine($"[{Theme.Chrome}]{MarkupText.Escape(caption)}[/][{color}]{fill}[/][{Theme.Rule}]{rest}[/]", caption + fill + rest);
    }

    private string Status() => _state is null ? "No objective"
        : DisplayCase.Token(PursueState.StatusToken(_state.Status))
            + (_state.Phase == PursuePhase.None ? string.Empty : " / " + DisplayCase.Token(PursueState.PhaseToken(_state.Phase)))
            + $"   {_state.Checks:N0} {(_state.Checks == 1 ? "check" : "checks")} this run"
            + (_state.Reason.Length > 0 ? "   " + _state.Reason : string.Empty);

    private string IdleHint() => _state?.Status switch
    {
        PursueStatus.Active => "Pursue is active. Work continues while you inspect checks.",
        PursueStatus.Paused or PursueStatus.Blocked => "R resumes this objective. Esc returns to the conversation.",
        PursueStatus.Complete => "Objective complete. Esc returns to the conversation.",
        _ => "Read-only inspection. Esc returns to the conversation."
    };

    private string StatusStyle() => _state?.Status switch
    {
        PursueStatus.Complete => Theme.Ok,
        PursueStatus.Paused or PursueStatus.Blocked => Theme.Warning,
        PursueStatus.Active => Theme.Accent,
        _ => Theme.Chrome
    };

    private static string VerdictStyle(string verdict) => verdict switch
    {
        "done" => Theme.Ok,
        "failed" => Theme.Fail,
        "blocked" or "wait" => Theme.Warning,
        _ => Theme.Chrome
    };

    private static string Color(string style) => style switch
    {
        "heading" => Theme.Heading,
        "muted" => Theme.Muted,
        _ => Theme.User
    };

    private static PaintLine Line(string color, string text, int width) =>
        PaintLine.Colored(color, TextWidth.Truncate(TerminalText.SanitizeLine(text), Math.Max(1, width)));

    private static PaintLine Pad(PaintLine line, int width)
    {
        var fitted = line.Fit(width);
        var pad = new string(' ', Math.Max(0, width - TextWidth.Measure(fitted.Plain)));
        return new PaintLine(fitted.Markup + pad, fitted.Plain + pad);
    }

    private static PaintLine Column(PaintLine content, int width, PaintLine cell)
    {
        var padded = Pad(content, Math.Max(1, width - TranscriptScrollbar.Columns));
        return Join(padded, new PaintLine(" ", " "), cell).Fit(width);
    }

    private static PaintLine Join(PaintLine left, PaintLine separator, PaintLine right) =>
        new(left.Markup + separator.Markup + right.Markup, left.Plain + separator.Plain + right.Plain);
}
