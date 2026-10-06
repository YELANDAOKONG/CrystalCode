using System.Text;
using CrystalCode.Display.Paint;
using Spectre.Console.Rendering;

namespace CrystalCode.Display.Transcript;

/// <summary>
/// Committed transcript plus one live streaming block.
/// Committed rows are cached by width. A live card keeps finished rows and
/// reflows only its open tail, so scroll and input do not rasterize the block again.
/// </summary>
public sealed class TranscriptLog
{
    private const int IndentColumns = 2;
    private readonly List<TranscriptEntry> _entries = [];
    private readonly StringBuilder _live = new();
    private readonly LivePanelCache _livePanel = new();
    private TranscriptKind? _liveKind;
    private IReadOnlyList<PaintLine> _otherLive = [];
    private TranscriptKind? _otherLiveKind;
    private int _otherLiveWidth;
    private int _otherLiveLength = -1;
    private TerminalText.StreamState _streamState;

    private int _cachedWidth;
    private readonly List<PaintLine> _committedLines = [];

    // Row counts parallel to _entries at the cached width. A zero count is a
    // hidden block, so an anchor can skip it and find the same later row.
    private readonly List<int> _entryRows = [];
    private bool _verboseTools = true;
    private bool _verboseCommands = true;
    private bool _verboseApprovals = true;
    private bool _verboseThinking = true;

    public bool VerboseTools
    {
        get => _verboseTools;
        set
        {
            if (_verboseTools == value)
            {
                return;
            }

            _verboseTools = value;
            InvalidateCache();
        }
    }

    public bool VerboseCommands
    {
        get => _verboseCommands;
        set
        {
            if (_verboseCommands == value)
            {
                return;
            }

            _verboseCommands = value;
            InvalidateCache();
        }
    }

    public bool VerboseApprovals
    {
        get => _verboseApprovals;
        set
        {
            if (_verboseApprovals == value)
            {
                return;
            }

            _verboseApprovals = value;
            InvalidateCache();
        }
    }

    public bool VerboseThinking
    {
        get => _verboseThinking;
        set
        {
            if (_verboseThinking == value)
            {
                return;
            }

            _verboseThinking = value;
            InvalidateCache();
        }
    }

    /// <summary>
    /// Inline row text when the alternate screen is off. Null when a verbose
    /// switch hides the row. Command results stay, compacted when that switch is off.
    /// </summary>
    public string? InlineText(TranscriptKind kind, string text, string? toolName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (HidesThinking(kind) || (kind == TranscriptKind.Approval && !_verboseApprovals))
        {
            return null;
        }

        if (kind is not (TranscriptKind.Result or TranscriptKind.Error))
        {
            return text;
        }

        if (!TranscriptResultDisplay.ShouldRender(
            kind,
            toolName,
            _verboseTools,
            _verboseCommands))
        {
            return null;
        }

        var display = TranscriptResultDisplay.Text(
            kind,
            text,
            toolName,
            _verboseTools,
            _verboseCommands);
        return display.Length == 0 ? null : display;
    }

    public void Add(
        TranscriptKind kind,
        string text,
        IRenderable? widget = null,
        string? toolName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        CommitLive();
        text = TerminalText.Sanitize(text);
        if (text.Length == 0 && widget is null)
        {
            return;
        }

        var entry = new TranscriptEntry(kind, text, widget, toolName);
        _entries.Add(entry);
        if (_cachedWidth > 0)
        {
            AppendCommitted(RenderEntry(entry, _cachedWidth));
        }
    }

    public string AppendLive(TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (_liveKind != kind)
        {
            CommitLive();
            _liveKind = kind;
        }

        // Scan the delta only. Clean text is returned unchanged, and a control
        // sequence is removed without rewriting characters already in _live.
        text = TerminalText.SanitizeStream(text, ref _streamState);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        _live.Append(text);
        return text;
    }

    public void CommitLive()
    {
        _streamState.Reset();
        if (_liveKind is null)
        {
            return;
        }

        if (_live.Length > 0)
        {
            var entry = new TranscriptEntry(_liveKind.Value, _live.ToString());
            _entries.Add(entry);
            if (_cachedWidth > 0)
            {
                var ready = ReadyLiveLines(_cachedWidth);
                AppendCommitted(ready ?? RenderEntry(entry, _cachedWidth));
            }
        }

        _live.Clear();
        _liveKind = null;
        _livePanel.Clear();
        ClearOtherLive();
    }

    public void DiscardLive()
    {
        _streamState.Reset();
        _live.Clear();
        _liveKind = null;
        _livePanel.Clear();
        ClearOtherLive();
    }

    public void Clear()
    {
        _entries.Clear();
        _committedLines.Clear();
        _entryRows.Clear();
        _cachedWidth = 0;
        _streamState.Reset();
        _live.Clear();
        _liveKind = null;
        _livePanel.Clear();
        ClearOtherLive();
    }

    public void InvalidateCache() => _cachedWidth = 0;

    public IReadOnlyList<PaintLine> Viewport(int width, int rows, int scrollBack)
    {
        EnsureCommittedLines(width);
        var live = LiveLines(width);
        var total = _committedLines.Count + live.Count;
        var maxScroll = Math.Max(0, total - rows);
        var back = Math.Clamp(scrollBack, 0, maxScroll);
        var take = Math.Min(rows, total);
        var start = Math.Max(0, total - take - back);
        var visible = new List<PaintLine>(Math.Max(rows, 0));
        var pad = rows - Math.Min(rows, total - start);
        for (var i = 0; i < pad; i++)
        {
            visible.Add(PaintLine.Blank);
        }

        var end = Math.Min(total, start + rows - pad);
        for (var i = start; i < end; i++)
        {
            visible.Add(LineAt(i, live));
        }

        return visible;
    }

    /// <summary>Committed rows plus the live block at this width.</summary>
    public int RowCount(int width)
    {
        EnsureCommittedLines(width);
        return _committedLines.Count + LiveLines(width).Count;
    }

    public int ClampScroll(int width, int rows, int scrollBack)
    {
        EnsureCommittedLines(width);
        var count = _committedLines.Count + LiveLines(width).Count;
        return Math.Clamp(scrollBack, 0, Math.Max(0, count - rows));
    }

    /// <summary>
    /// The committed row at this content index. A live tail is marked with the
    /// entry index it will occupy once committed.
    /// </summary>
    internal ScrollMark MarkAt(int width, int contentRow)
    {
        EnsureCommittedLines(width);
        var live = LiveLines(width);
        var committed = _committedLines.Count;
        var total = committed + live.Count;
        if (total == 0)
        {
            return new ScrollMark(0, 0);
        }

        var row = Math.Clamp(contentRow, 0, total - 1);
        if (row >= committed)
        {
            return new ScrollMark(_entries.Count, row - committed);
        }

        var index = 0;
        for (var i = 0; i < _entryRows.Count; i++)
        {
            var rows = _entryRows[i];
            if (rows == 0)
            {
                continue;
            }

            if (row < index + rows)
            {
                return new ScrollMark(i, row - index);
            }

            index += rows;
        }

        return new ScrollMark(_entries.Count, 0);
    }

    /// <summary>
    /// Where the marked row sits after later output or an earlier block
    /// changed height. A hidden entry resolves to the next visible row.
    /// </summary>
    internal int RowOf(int width, ScrollMark mark)
    {
        EnsureCommittedLines(width);
        var live = LiveLines(width);
        var committed = _committedLines.Count;
        var total = committed + live.Count;
        if (total == 0)
        {
            return 0;
        }

        var last = total - 1;
        if (mark.EntryIndex == _entries.Count && live.Count > 0)
        {
            return committed + Math.Clamp(mark.Line, 0, live.Count - 1);
        }

        if ((uint)mark.EntryIndex >= (uint)_entryRows.Count)
        {
            return last;
        }

        var entryRows = _entryRows[mark.EntryIndex];
        if (entryRows == 0)
        {
            return NextContentRow(mark.EntryIndex, total);
        }

        return EntryStart(mark.EntryIndex) + Math.Clamp(mark.Line, 0, entryRows - 1);
    }

    public IReadOnlyList<PaintLine> BuildLines(int width)
    {
        EnsureCommittedLines(width);
        var live = LiveLines(width);
        if (live.Count == 0)
        {
            return _committedLines;
        }

        var combined = new List<PaintLine>(_committedLines.Count + live.Count);
        combined.AddRange(_committedLines);
        combined.AddRange(live);
        return combined;
    }

    private PaintLine LineAt(int index, IReadOnlyList<PaintLine> live) =>
        index < _committedLines.Count
            ? _committedLines[index]
            : live[index - _committedLines.Count];

    private IReadOnlyList<PaintLine> LiveLines(int width)
    {
        if (_liveKind is null || _live.Length == 0 || HidesThinking(_liveKind.Value))
        {
            return [];
        }

        if (PanelLines.Supports(_liveKind.Value))
        {
            return _livePanel.Update(_liveKind.Value, _live, width);
        }

        if (_otherLiveKind == _liveKind
            && _otherLiveWidth == width
            && _otherLiveLength == _live.Length)
        {
            return _otherLive;
        }

        var entry = new TranscriptEntry(_liveKind.Value, _live.ToString());
        _otherLive = RenderEntry(entry, width);
        _otherLiveKind = _liveKind;
        _otherLiveWidth = width;
        _otherLiveLength = _live.Length;
        return _otherLive;
    }

    private IReadOnlyList<PaintLine>? ReadyLiveLines(int width)
    {
        if (_liveKind is null || _live.Length == 0 || HidesThinking(_liveKind.Value))
        {
            return null;
        }

        if (PanelLines.Supports(_liveKind.Value))
        {
            return _livePanel.LinesIfReady(_liveKind.Value, _live.Length, width);
        }

        if (_otherLiveKind == _liveKind
            && _otherLiveWidth == width
            && _otherLiveLength == _live.Length)
        {
            return _otherLive;
        }

        return null;
    }

    private void ClearOtherLive()
    {
        _otherLive = [];
        _otherLiveKind = null;
        _otherLiveWidth = 0;
        _otherLiveLength = -1;
    }

    private void AppendCommitted(IReadOnlyList<PaintLine> lines)
    {
        _committedLines.AddRange(lines);
        _entryRows.Add(lines.Count);
    }

    private int EntryStart(int entryIndex)
    {
        var index = 0;
        var limit = Math.Min(entryIndex, _entryRows.Count);
        for (var i = 0; i < limit; i++)
        {
            index += _entryRows[i];
        }

        return index;
    }

    private int NextContentRow(int entryIndex, int total)
    {
        for (var i = entryIndex; i < _entryRows.Count; i++)
        {
            if (_entryRows[i] > 0)
            {
                return EntryStart(i);
            }
        }

        return Math.Max(0, total - 1);
    }

    private void EnsureCommittedLines(int width)
    {
        if (_cachedWidth == width && _entryRows.Count == _entries.Count)
        {
            return;
        }

        _cachedWidth = width;
        _committedLines.Clear();
        _entryRows.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            AppendCommitted(RenderEntry(_entries[i], width));
        }
    }

    private IReadOnlyList<PaintLine> RenderEntry(
        TranscriptEntry entry,
        int width)
    {
        var bodyWidth = Math.Max(width - IndentColumns, 1);
        var indent = new string(' ', IndentColumns);
        var lines = new List<PaintLine>();

        if (entry.Kind == TranscriptKind.Approval && !_verboseApprovals)
        {
            return lines;
        }

        if (HidesThinking(entry.Kind))
        {
            return lines;
        }

        if (entry.Widget is not null)
        {
            lines.AddRange(WidgetPaint.Lines(entry.Widget, width));
            return lines;
        }

        if (entry.Kind == TranscriptKind.Assistant)
        {
            lines.AddRange(MarkdownRenderer.Render(entry.Text, width));
            return lines;
        }

        if (entry.Kind is TranscriptKind.Result or TranscriptKind.Error)
        {
            var displayText = InlineText(entry.Kind, entry.Text, entry.ToolName);
            if (displayText is null)
            {
                return lines;
            }

            if (entry.Kind == TranscriptKind.Error)
            {
                return PanelLines.Create(entry.Kind, displayText, width);
            }

            var resultCard = TranscriptCard.TryCreate(entry.Kind, displayText);
            if (resultCard is not null)
            {
                lines.AddRange(WidgetPaint.Lines(resultCard, width));
                return lines;
            }
        }

        var panel = PanelLines.TryCreate(entry.Kind, entry.Text, width);
        if (panel is not null)
        {
            return panel;
        }

        var color = ColorFor(entry.Kind);
        foreach (var wrapped in TextWidth.Wrap(entry.Text, bodyWidth))
        {
            var plain = indent + wrapped;
            if (TextWidth.Measure(plain) > width)
            {
                plain = TextWidth.Truncate(plain, width);
            }

            lines.Add(PaintLine.Colored(color, plain));
        }

        return lines;
    }

    private bool HidesThinking(TranscriptKind kind) =>
        kind == TranscriptKind.Thinking && !_verboseThinking;

    private static string ColorFor(TranscriptKind kind) =>
        kind switch
        {
            TranscriptKind.User => Theme.User,
            TranscriptKind.Assistant => Theme.User,
            TranscriptKind.Thinking => Theme.Thinking,
            TranscriptKind.Tool => Theme.Tool,
            TranscriptKind.Result => Theme.Ok,
            TranscriptKind.Note => Theme.Chrome,
            TranscriptKind.Error => Theme.Fail,
            TranscriptKind.Approval => Theme.Review,
            TranscriptKind.Summary => Theme.User,
            _ => Theme.Chrome
        };
}
