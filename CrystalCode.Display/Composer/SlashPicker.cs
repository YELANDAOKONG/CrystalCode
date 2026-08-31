using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Composer;

/// <summary>
/// Filtered slash list for a composer prefix.
/// </summary>
public sealed class SlashPicker
{
    public const int MaximumVisible = 8;

    private readonly IReadOnlyList<SlashOption> _matches;
    private readonly int _selected;
    private readonly string _completedPrefix;
    private readonly bool _arguments;

    private SlashPicker(
        IReadOnlyList<SlashOption> matches,
        int selected,
        string completedPrefix,
        bool arguments)
    {
        _matches = matches;
        _selected = selected;
        _completedPrefix = completedPrefix;
        _arguments = arguments;
    }

    public IReadOnlyList<SlashOption> Matches => _matches;

    public int Selected => _selected;

    public string CompletedText => _completedPrefix + _matches[_selected].Name + " ";

    public static SlashPicker? Create(string text, IReadOnlyList<SlashOption> options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        if (text.Length == 0 || text[0] != '/' || text.Contains('\n', StringComparison.Ordinal))
        {
            return null;
        }

        var rest = text[1..];
        var space = rest.IndexOf(' ');
        if (space >= 0)
        {
            return CreateArguments(rest, space, options);
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

        return matches.Count == 0
            ? null
            : new SlashPicker(matches, 0, "/", arguments: false);
    }

    public SlashPicker Move(int delta)
    {
        var count = _matches.Count;
        var next = (_selected + delta) % count;
        if (next < 0)
        {
            next += count;
        }

        return new SlashPicker(_matches, next, _completedPrefix, _arguments);
    }

    private static SlashPicker? CreateArguments(
        string rest,
        int space,
        IReadOnlyList<SlashOption> options)
    {
        var verb = rest[..space];
        var remainder = rest[(space + 1)..];
        var command = FindCommand(verb, options);
        if (command is null || command.ArgumentOptions.Count == 0)
        {
            return null;
        }

        var nestedSpace = remainder.IndexOf(' ');
        if (nestedSpace < 0)
        {
            return FilterArguments(command.ArgumentOptions, remainder, "/" + verb + " ");
        }

        var first = remainder[..nestedSpace];
        var nestedRemainder = remainder[(nestedSpace + 1)..];
        if (nestedRemainder.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        var nested = FindNestedCommand(first, command.ArgumentOptions);
        if (nested is null || nested.ArgumentOptions.Count == 0)
        {
            return null;
        }

        return FilterArguments(
            nested.ArgumentOptions,
            nestedRemainder,
            "/" + verb + " " + first + " ");
    }

    private static SlashPicker? FilterArguments(
        IReadOnlyList<SlashOption> arguments,
        string prefixSource,
        string completedPrefix)
    {
        var prefix = prefixSource.ToLowerInvariant();
        var matches = new List<SlashOption>();
        foreach (var option in arguments)
        {
            if (MatchesPrefix(option, prefix))
            {
                matches.Add(option);
            }
        }

        return matches.Count == 0
            ? null
            : new SlashPicker(matches, 0, completedPrefix, arguments: true);
    }

    private static SlashOption? FindNestedCommand(string verb, IReadOnlyList<SlashOption> options)
    {
        SlashOption? fallback = null;
        foreach (var option in options)
        {
            foreach (var key in option.Keys)
            {
                if (!string.Equals(key, verb, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (option.ArgumentOptions.Count > 0)
                {
                    return option;
                }

                fallback ??= option;
            }
        }

        return fallback;
    }

    private static SlashOption? FindCommand(string verb, IReadOnlyList<SlashOption> options)
    {
        foreach (var option in options)
        {
            foreach (var key in option.Keys)
            {
                if (string.Equals(key, verb, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }
        }

        return null;
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
            var cmdName = _arguments ? option.Name : "/" + option.Name;
            var paddedCmd = cmdName.PadRight(maxCmdLen);
            var aliases = AliasesLabel(option, _arguments);

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

    private static string AliasesLabel(SlashOption option, bool arguments)
    {
        var parts = new List<string>();
        foreach (var key in option.Keys)
        {
            if (!string.Equals(key, option.Name, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(arguments ? key : "/" + key);
            }
        }

        return parts.Count == 0 ? string.Empty : "(" + string.Join(", ", parts) + ")";
    }
}
