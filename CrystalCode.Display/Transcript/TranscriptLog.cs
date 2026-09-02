using System.Text;
using CrystalCode.Display.Paint;
using Spectre.Console.Rendering;

namespace CrystalCode.Display.Transcript;

/// <summary>
/// Committed transcript plus one live streaming block.
/// Caches rendered paint lines for committed entries to ensure smooth scrolling and frame updates.
/// </summary>
public sealed class TranscriptLog
{
    private const int IndentColumns = 2;
    private readonly List<TranscriptEntry> _entries = [];
    private readonly StringBuilder _live = new();
    private TranscriptKind? _liveKind;

    private int _cachedWidth;
    private readonly List<PaintLine> _committedLines = [];
    private bool _verboseTools = true;
    private bool _verboseCommands = true;

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

    public void Add(
        TranscriptKind kind,
        string text,
        IRenderable? widget = null,
        string? toolName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        CommitLive();
        if (text.Length == 0 && widget is null)
        {
            return;
        }

        var entry = new TranscriptEntry(kind, text, widget, toolName);
        _entries.Add(entry);
        if (_cachedWidth > 0)
        {
            _committedLines.AddRange(RenderEntry(entry, _cachedWidth));
        }
    }

    public void AppendLive(TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        if (_liveKind != kind)
        {
            CommitLive();
            _liveKind = kind;
        }

        _live.Append(text);
    }

    public void CommitLive()
    {
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
                _committedLines.AddRange(RenderEntry(entry, _cachedWidth));
            }
        }

        _live.Clear();
        _liveKind = null;
    }

    public void DiscardLive()
    {
        _live.Clear();
        _liveKind = null;
    }

    public void Clear()
    {
        _entries.Clear();
        _committedLines.Clear();
        _cachedWidth = 0;
        _live.Clear();
        _liveKind = null;
    }

    public void InvalidateCache() => _cachedWidth = 0;

    public IReadOnlyList<PaintLine> Viewport(int width, int rows, int scrollBack)
    {
        var all = BuildLines(width);
        var maxScroll = Math.Max(0, all.Count - rows);
        var back = Math.Clamp(scrollBack, 0, maxScroll);
        var take = Math.Min(rows, all.Count);
        var start = Math.Max(0, all.Count - take - back);
        var visible = new List<PaintLine>(rows);
        var pad = rows - Math.Min(rows, all.Count - start);
        for (var i = 0; i < pad; i++)
        {
            visible.Add(PaintLine.Blank);
        }

        var end = Math.Min(all.Count, start + rows - pad);
        for (var i = start; i < end; i++)
        {
            visible.Add(all[i]);
        }

        return visible;
    }

    public int ClampScroll(int width, int rows, int scrollBack)
    {
        EnsureCommittedLines(width);
        var count = _committedLines.Count;
        if (_liveKind is not null && _live.Length > 0)
        {
            var liveEntry = new TranscriptEntry(_liveKind.Value, _live.ToString());
            count += RenderEntry(liveEntry, width).Count;
        }

        return Math.Clamp(scrollBack, 0, Math.Max(0, count - rows));
    }

    public IReadOnlyList<PaintLine> BuildLines(int width)
    {
        EnsureCommittedLines(width);

        if (_liveKind is null || _live.Length == 0)
        {
            return _committedLines;
        }

        var liveEntry = new TranscriptEntry(_liveKind.Value, _live.ToString());
        var liveLines = RenderEntry(liveEntry, width);
        var combined = new List<PaintLine>(_committedLines.Count + liveLines.Count);
        combined.AddRange(_committedLines);
        combined.AddRange(liveLines);
        return combined;
    }

    private void EnsureCommittedLines(int width)
    {
        if (_cachedWidth == width)
        {
            return;
        }

        _cachedWidth = width;
        _committedLines.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            _committedLines.AddRange(RenderEntry(_entries[i], width));
        }
    }

    private IReadOnlyList<PaintLine> RenderEntry(
        TranscriptEntry entry,
        int width)
    {
        var bodyWidth = Math.Max(width - IndentColumns, 1);
        var indent = new string(' ', IndentColumns);
        var lines = new List<PaintLine>();

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
            if (!TranscriptResultDisplay.ShouldRender(
                entry.Kind,
                entry.ToolName,
                _verboseTools,
                _verboseCommands))
            {
                return lines;
            }

            var displayText = TranscriptResultDisplay.Text(
                entry.Kind,
                entry.Text,
                entry.ToolName,
                _verboseTools,
                _verboseCommands);
            if (displayText.Length == 0)
            {
                return lines;
            }

            var resultCard = TranscriptCard.TryCreate(entry.Kind, displayText);
            if (resultCard is not null)
            {
                lines.AddRange(WidgetPaint.Lines(resultCard, width));
                return lines;
            }
        }

        var panel = TranscriptCard.TryCreate(entry.Kind, entry.Text);
        if (panel is not null)
        {
            lines.AddRange(WidgetPaint.Lines(panel, width));
            return lines;
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
            _ => Theme.Chrome
        };
}
