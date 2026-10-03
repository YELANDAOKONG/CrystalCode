using System.Text;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Transcript;

/// <summary>
/// Append-only card rows. Finished lines stay put; only the open tail is
/// reflowed, so scrolling and typing do not rasterize a long thinking block.
/// </summary>
internal sealed class LivePanelCache
{
    private readonly List<PanelLines.WrappedSegment> _body = [];
    private readonly List<PaintLine> _lines = [];
    private TranscriptKind _kind;
    private int _width;
    private int _length;
    private string _border = string.Empty;
    private string _color = string.Empty;
    private int _contentWidth;

    public void Clear()
    {
        _body.Clear();
        _lines.Clear();
        _kind = default;
        _width = 0;
        _length = 0;
        _contentWidth = 0;
    }

    public IReadOnlyList<PaintLine>? LinesIfReady(TranscriptKind kind, int length, int width)
    {
        var panelWidth = PanelLines.PanelWidth(width);
        if (kind == _kind && length == _length && panelWidth == _width && _lines.Count > 0)
        {
            return _lines;
        }

        return null;
    }

    public IReadOnlyList<PaintLine> Update(TranscriptKind kind, StringBuilder text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        var panelWidth = PanelLines.PanelWidth(width);
        if (text.Length == 0)
        {
            Clear();
            return _lines;
        }

        if (kind != _kind || panelWidth != _width || text.Length < _length || _lines.Count == 0)
        {
            Rebuild(kind, text, panelWidth);
            return _lines;
        }

        if (text.Length == _length)
        {
            return _lines;
        }

        Extend(text);
        return _lines;
    }

    private void Rebuild(TranscriptKind kind, StringBuilder text, int panelWidth)
    {
        _kind = kind;
        _width = panelWidth;
        _length = text.Length;
        _border = TranscriptCard.BorderColor(kind);
        _color = TranscriptCard.Color(kind);
        _contentWidth = PanelLines.ContentWidth(panelWidth);
        _body.Clear();
        PanelLines.Wrap(text.ToString(), _contentWidth, 0, _body);
        WriteAll();
    }

    private void Extend(StringBuilder text)
    {
        if (_body.Count == 0 || _lines.Count != _body.Count + 2)
        {
            Rebuild(_kind, text, _width);
            return;
        }

        var start = _body[^1].Start;
        if (start < 0 || start > text.Length)
        {
            Rebuild(_kind, text, _width);
            return;
        }

        var paintFrom = _body.Count;
        _body.RemoveAt(_body.Count - 1);
        var insertAt = _body.Count;
        var suffix = text.ToString(start, text.Length - start);
        PanelLines.Wrap(suffix, _contentWidth, start, _body);
        _lines.RemoveRange(paintFrom, _lines.Count - paintFrom);
        for (var i = insertAt; i < _body.Count; i++)
        {
            _lines.Add(PanelLines.Body(_border, _color, _body[i].Text, _contentWidth));
        }

        _lines.Add(PanelLines.Bottom(_border, _width));
        _length = text.Length;
    }

    private void WriteAll()
    {
        _lines.Clear();
        var header = TranscriptCard.Header(_kind) ?? string.Empty;
        _lines.Add(PanelLines.Top(_border, header, _width));
        for (var i = 0; i < _body.Count; i++)
        {
            _lines.Add(PanelLines.Body(_border, _color, _body[i].Text, _contentWidth));
        }

        _lines.Add(PanelLines.Bottom(_border, _width));
    }
}
