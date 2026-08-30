using CrystalHarness.Tools;

namespace CrystalHarness.Display;

/// <summary>
/// One-line tool call for the transcript. Never dumps streaming JSON fragments.
/// </summary>
public static class ToolCallText
{
    public static string Summary(string name, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var title = DisplayCase.Token(name);
        if (ToolArguments.TryReadRequiredString(arguments, "command", out var command))
        {
            return title + "  " + OneLine(command);
        }

        if (ToolArguments.TryReadRequiredString(arguments, "path", out var path))
        {
            if (ToolArguments.TryReadRequiredString(arguments, "pattern", out var pattern))
            {
                return title + "  " + path + "  " + OneLine(pattern);
            }

            return title + "  " + path;
        }

        if (ToolArguments.TryReadRequiredString(arguments, "pattern", out var onlyPattern))
        {
            return title + "  " + OneLine(onlyPattern);
        }

        var compact = CompactFinished(arguments);
        return compact.Length == 0 ? title : title + "  " + compact;
    }

    private static string CompactFinished(string arguments)
    {
        var flat = OneLine(arguments);
        if (flat is "{}" or "")
        {
            return string.Empty;
        }

        if (!flat.StartsWith('{') || !flat.EndsWith('}'))
        {
            return string.Empty;
        }

        return flat.Length <= 80 ? flat : flat[..77] + "...";
    }

    private static string OneLine(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
