using System.Text;

namespace CrystalHarness.Display;

/// <summary>
/// Committed transcript plus one live streaming block.
/// </summary>
public sealed class TranscriptLog
{
    private const int IndentColumns = 2;
    private readonly List<TranscriptEntry> _entries = [];
    private readonly StringBuilder _live = new();
    private TranscriptKind? _liveKind;

    public void Add(TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        CommitLive();
        if (text.Length == 0)
        {
            return;
        }

        _entries.Add(new TranscriptEntry(kind, text));
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
            _entries.Add(new TranscriptEntry(_liveKind.Value, _live.ToString()));
        }

        _live.Clear();
        _liveKind = null;
    }

    public void Clear()
    {
        _entries.Clear();
        _live.Clear();
        _liveKind = null;
    }

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
        var count = BuildLines(width).Count;
        return Math.Clamp(scrollBack, 0, Math.Max(0, count - rows));
    }

    public IReadOnlyList<PaintLine> BuildLines(int width)
    {
        CommitSnapshot(out var entries, out var lastIsLive);
        var bodyWidth = Math.Max(width - IndentColumns, 1);
        var indent = new string(' ', IndentColumns);
        var lines = new List<PaintLine>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var liveAssistant = lastIsLive
                && i == entries.Count - 1
                && entry.Kind == TranscriptKind.Assistant;
            if (entry.Kind == TranscriptKind.Assistant && !liveAssistant)
            {
                lines.AddRange(MarkdownRenderer.Render(entry.Text, width));
                continue;
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
        }

        return lines;
    }

    private void CommitSnapshot(out List<TranscriptEntry> entries, out bool lastIsLive)
    {
        entries = [.. _entries];
        lastIsLive = _liveKind is not null && _live.Length > 0;
        if (lastIsLive)
        {
            entries.Add(new TranscriptEntry(_liveKind!.Value, _live.ToString()));
        }
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
