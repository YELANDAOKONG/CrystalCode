namespace CrystalHarness.Display;

/// <summary>
/// Filtered slash list for a composer prefix.
/// </summary>
public sealed class SlashPicker
{
    public const int MaximumVisible = 8;

    private readonly IReadOnlyList<SlashOption> _matches;
    private readonly int _selected;

    private SlashPicker(IReadOnlyList<SlashOption> matches, int selected)
    {
        _matches = matches;
        _selected = selected;
    }

    public IReadOnlyList<SlashOption> Matches => _matches;

    public int Selected => _selected;

    public string CompletedText => "/" + _matches[_selected].Name + " ";

    public static SlashPicker? Create(string text, IReadOnlyList<SlashOption> options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        if (text.Length == 0 || text[0] != '/' || text.Contains('\n', StringComparison.Ordinal))
        {
            return null;
        }

        var rest = text[1..];
        if (rest.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        var prefix = rest.ToLowerInvariant();
        var matches = new List<SlashOption>();
        foreach (var option in options)
        {
            if (MatchesPrefix(option, prefix))
            {
                matches.Add(option);
            }
        }

        return matches.Count == 0 ? null : new SlashPicker(matches, 0);
    }

    public SlashPicker Move(int delta)
    {
        var count = _matches.Count;
        var next = (_selected + delta) % count;
        if (next < 0)
        {
            next += count;
        }

        return new SlashPicker(_matches, next);
    }

    public IReadOnlyList<PaintLine> Paint(int width)
    {
        var visible = Math.Min(_matches.Count, MaximumVisible);
        var lines = new List<PaintLine>(visible);
        for (var i = 0; i < visible; i++)
        {
            var option = _matches[i];
            var mark = i == _selected ? ">" : " ";
            var aliases = AliasesLabel(option);
            var plain = string.IsNullOrEmpty(aliases)
                ? $"  {mark} /{option.Name}  {option.Help}"
                : $"  {mark} /{option.Name}  {aliases}  {option.Help}";
            var color = i == _selected ? Theme.Selected : Theme.Chrome;
            lines.Add(PaintLine.Colored(color, TextWidth.Truncate(plain, width)));
        }

        return lines;
    }

    private static bool MatchesPrefix(SlashOption option, string prefix)
    {
        foreach (var key in option.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string AliasesLabel(SlashOption option)
    {
        var parts = new List<string>();
        foreach (var key in option.Keys)
        {
            if (!string.Equals(key, option.Name, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("/" + key);
            }
        }

        return string.Join("  ", parts);
    }
}
