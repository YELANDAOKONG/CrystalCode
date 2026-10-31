using System.Text;

namespace CrystalCode.Display.Paint;

/// <summary>
/// Markup for removed and added diff lines: the whole line on a tinted
/// background, and the words that changed on a stronger one. A line with no
/// counterpart gets the line tint only.
/// </summary>
public static class DiffLines
{
    /// <summary>
    /// Pairs the removed and added lines of one hunk by position and returns
    /// one line per input, removed lines first. Extra lines on either side
    /// have no partner.
    /// </summary>
    public static IReadOnlyList<PaintLine> Hunk(IReadOnlyList<string> removed, IReadOnlyList<string> added)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(added);
        var lines = new List<PaintLine>(removed.Count + added.Count);
        for (var index = 0; index < removed.Count; index++)
        {
            var partner = index < added.Count ? added[index] : null;
            lines.Add(Removed(removed[index], partner));
        }

        for (var index = 0; index < added.Count; index++)
        {
            var partner = index < removed.Count ? removed[index] : null;
            lines.Add(Added(added[index], partner));
        }

        return lines;
    }

    public static PaintLine Removed(string text, string? partner = null) =>
        Line('-', text, partner, Theme.DiffRemoved, Theme.DiffRemovedBg, Theme.DiffRemovedWord, oldSide: true);

    public static PaintLine Added(string text, string? partner = null) =>
        Line('+', text, partner, Theme.DiffAdded, Theme.DiffAddedBg, Theme.DiffAddedWord, oldSide: false);

    private static PaintLine Line(
        char prefix,
        string text,
        string? partner,
        string foreground,
        string lineBackground,
        string wordBackground,
        bool oldSide)
    {
        var plain = prefix + text;
        var span = partner is null
            ? null
            : oldSide
                ? DiffSpan.Between(text, partner)
                : DiffSpan.Between(partner, text);
        var markup = new StringBuilder();
        if (span is not { } found)
        {
            Append(markup, foreground, lineBackground, plain);
            return new PaintLine(markup.ToString(), plain);
        }

        var start = oldSide ? found.OldStart : found.NewStart;
        var length = oldSide ? found.OldLength : found.NewLength;
        Append(markup, foreground, lineBackground, prefix + text[..start]);
        Append(markup, foreground + " bold", wordBackground, text.Substring(start, length));
        Append(markup, foreground, lineBackground, text[(start + length)..]);
        return new PaintLine(markup.ToString(), plain);
    }

    private static void Append(StringBuilder markup, string foreground, string background, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        markup.Append('[').Append(foreground).Append(" on ").Append(background).Append(']')
            .Append(MarkupText.Escape(text)).Append("[/]");
    }
}
