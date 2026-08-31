using Crystal.Tools;

using CrystalHarness.Display.Paint;
using CrystalHarness.Tools;

namespace CrystalHarness.Approvals;

/// <summary>
/// Edit and write argument preview for approval cards. Caps long bodies.
/// </summary>
public static class ApprovalDiff
{
    public const int MaximumLines = 12;

    public static IReadOnlyList<(string Color, string Text)> Lines(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (ToolArguments.TryReadRequiredString(call.Arguments, "old_string", out var oldText)
            && ToolArguments.TryReadRequiredString(call.Arguments, "new_string", out var newText))
        {
            var lines = new List<(string Color, string Text)>();
            AddPrefixed(lines, oldText, '-', Theme.DiffRemoved);
            AddPrefixed(lines, newText, '+', Theme.DiffAdded);
            return lines;
        }

        if (ToolArguments.TryReadRequiredString(call.Arguments, "contents", out var contents))
        {
            var lines = new List<(string Color, string Text)>();
            AddPrefixed(lines, contents, '+', Theme.DiffAdded);
            return lines;
        }

        return [];
    }

    private static void AddPrefixed(
        List<(string Color, string Text)> lines,
        string body,
        char prefix,
        string color)
    {
        var raw = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var shown = 0;
        var total = 0;
        foreach (var line in raw)
        {
            total++;
            if (shown >= MaximumLines)
            {
                continue;
            }

            lines.Add((color, prefix + line));
            shown++;
        }

        if (total > MaximumLines)
        {
            lines.Add((Theme.Muted, $"... ({total - MaximumLines} more lines)"));
        }
    }
}
