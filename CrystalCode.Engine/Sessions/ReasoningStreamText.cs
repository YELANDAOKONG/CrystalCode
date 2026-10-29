using Crystal.Chat;
using Crystal.Reasoning;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Readable reasoning text for front ends. Providers stream separate
/// reasoning parts of one item, such as Responses summary parts, as separate
/// segments whose text does not end in a line break, so a front end that
/// appends the deltas must insert <see cref="Separator"/> where one segment
/// ends and the next begins. A change of item alone adds nothing, because
/// some providers, such as Gemini, give each streamed fragment of one thought
/// its own item. One instance follows one stream buffer and is not thread
/// safe; call <see cref="Reset"/> whenever that buffer is flushed or dropped.
/// </summary>
public sealed class ReasoningStreamText
{
    public const string Separator = "\n\n";

    private (int Candidate, int Item, int Segment)? _last;

    /// <summary>
    /// Returns the text to append for <paramref name="delta"/>: its text,
    /// preceded by <see cref="Separator"/> when it opens a new segment of the
    /// item that streamed last.
    /// </summary>
    public string Next(ChatReasoningTextDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (delta.Text.Length == 0)
        {
            return string.Empty;
        }

        var position = (delta.CandidateIndex, delta.ItemIndex, delta.TextSegmentIndex);
        var opensSegment = _last is { } last
            && last.Candidate == position.CandidateIndex
            && last.Item == position.ItemIndex
            && last.Segment != position.TextSegmentIndex;
        _last = position;
        return opensSegment ? Separator + delta.Text : delta.Text;
    }

    public void Reset() => _last = null;

    public static string Join(ReasoningContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return string.Join(Separator, content.TextSegments.Select(static segment => segment.Text));
    }
}
