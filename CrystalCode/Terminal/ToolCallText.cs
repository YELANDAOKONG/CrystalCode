using CrystalCode.Display.Paint;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Terminal;

/// <summary>
/// One-line tool call for the transcript. Never dumps streaming JSON fragments.
/// </summary>
public static class ToolCallText
{
    /// <summary>
    /// Model-supplied names, paths, and commands are shown with hidden
    /// characters spelled out, so an escape sequence or a bidirectional
    /// override cannot alter what an operator reads in an approval card.
    /// </summary>
    public static string Summary(string name, string arguments) =>
        TerminalText.Reveal(Flatten(SummaryCore(name, arguments)));

    private static string Flatten(string text) =>
        text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    private static string SummaryCore(string name, string arguments)
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

        if (ToolArguments.TryReadRequiredString(arguments, "filePath", out var filePath))
        {
            return title + "  " + filePath;
        }

        if (ToolArguments.TryReadRequiredString(arguments, "pattern", out var onlyPattern))
        {
            return title + "  " + OneLine(onlyPattern);
        }

        if (ToolArguments.TryReadRequiredString(arguments, "url", out var url))
        {
            return title + "  " + OneLine(url);
        }

        if (ToolArguments.TryReadRequiredString(arguments, "query", out var query))
        {
            return title + "  \"" + OneLine(query) + "\"";
        }

        if (ToolArguments.TryReadRequiredString(arguments, "name", out var skillName))
        {
            return title + "  " + OneLine(skillName);
        }

        if (ToolArguments.TryReadRequiredString(arguments, "description", out var desc))
        {
            return title + "  " + OneLine(desc);
        }

        if (ToolArguments.TryReadRequiredString(arguments, "taskId", out var taskId))
        {
            return title + "  " + OneLine(taskId);
        }

        if (name == ApplyPatchTool.ToolName)
        {
            // The argument is the patch text itself, so never fall back to
            // echoing it as JSON.
            var patchPaths = ApplyPatchTool.PathsOf(arguments);
            return patchPaths.Count > 0 ? title + "  " + PatchPaths(patchPaths) : title;
        }

        var compact = CompactFinished(arguments);
        return compact.Length == 0 ? title : title + "  " + compact;
    }

    private const int ShownPatchPaths = 3;

    private static string PatchPaths(IReadOnlyList<string> paths)
    {
        var unique = paths.Select(OneLine).Distinct(StringComparer.Ordinal).ToList();
        var listed = string.Join(", ", unique.Take(ShownPatchPaths));
        return unique.Count > ShownPatchPaths
            ? listed + $" and {unique.Count - ShownPatchPaths} more"
            : listed;
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
