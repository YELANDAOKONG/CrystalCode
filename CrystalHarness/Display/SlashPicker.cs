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

        var maxCmdLen = 0;
        for (var i = 0; i < visible; i++)
        {
            var len = _matches[i].Name.Length + 1;
            if (len > maxCmdLen)
            {
                maxCmdLen = len;
            }
        }

        maxCmdLen = Math.Max(maxCmdLen, 8);

        for (var i = 0; i < visible; i++)
        {
            var option = _matches[i];
            var isSelected = i == _selected;
            var mark = isSelected ? ">" : " ";
            var cmdName = "/" + option.Name;
            var paddedCmd = cmdName.PadRight(maxCmdLen);
            var aliases = AliasesLabel(option);

            var plain = string.IsNullOrEmpty(aliases)
                ? $"  {mark} {paddedCmd}  {option.Help}"
                : $"  {mark} {paddedCmd}  {aliases}  {option.Help}";

            string markup;
            if (isSelected)
            {
                var escapedPlain = MarkupText.Escape(plain);
                markup = $"[{Theme.Selected}]{escapedPlain}[/]";
            }
            else
            {
                var aliasMarkup = string.IsNullOrEmpty(aliases)
                    ? string.Empty
                    : $"[{Theme.Muted}]{MarkupText.Escape(aliases)}  [/]";
                markup = $"  [{Theme.Muted}]{mark}[/] "
                    + $"[{Theme.User}]{MarkupText.Escape(paddedCmd)}[/]  "
                    + aliasMarkup
                    + $"[{Theme.Chrome}]{MarkupText.Escape(option.Help)}[/]";
            }

            var truncatedPlain = TextWidth.Truncate(plain, width);
            if (truncatedPlain.Length < plain.Length)
            {
                markup = isSelected
                    ? $"[{Theme.Selected}]{MarkupText.Escape(truncatedPlain)}[/]"
                    : $"[{Theme.Chrome}]{MarkupText.Escape(truncatedPlain)}[/]";
            }

            lines.Add(new PaintLine(markup, truncatedPlain));
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

        return parts.Count == 0 ? string.Empty : "(" + string.Join(", ", parts) + ")";
    }
}
