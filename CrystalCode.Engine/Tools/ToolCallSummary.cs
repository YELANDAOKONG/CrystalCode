using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// The one-line title of a tool call: the Title Case tool name followed by
/// the argument that identifies the call (a command, a path, a pattern).
/// Partial JSON yields the bare name. Line breaks are flattened; a front end
/// decides how hidden characters are shown.
/// </summary>
public static class ToolCallSummary
{
    private const int ShownPatchPaths = 3;

    private const int MaximumCompactArguments = 80;

    public static string Text(string name, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return Flatten(Core(name, arguments));
    }

    private static string Core(string name, string arguments)
    {
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

        if (ToolArguments.TryReadRequiredString(arguments, "description", out var description))
        {
            return title + "  " + OneLine(description);
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

        return flat.Length <= MaximumCompactArguments
            ? flat
            : flat[..(MaximumCompactArguments - 3)] + "...";
    }

    private static string OneLine(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Flatten(string text) =>
        text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
}
