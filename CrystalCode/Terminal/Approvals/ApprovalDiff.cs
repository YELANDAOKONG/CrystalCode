using Crystal.Tools;
using CrystalCode.Display.Paint;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Terminal.Approvals;

/// <summary>
/// Edit and write argument preview for approval cards. Caps long bodies.
/// </summary>
public static class ApprovalDiff
{
    public const int MaximumLines = 12;

    public const string ReplaceAllNotice = "Replaces every occurrence";

    public static IReadOnlyList<(string Color, string Text)> Lines(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Name == ApplyPatchTool.ToolName)
        {
            return PatchLines(call);
        }

        if (ToolArguments.TryReadRequiredString(call.Arguments, "old_string", out var oldText)
            && ToolArguments.TryReadRequiredString(call.Arguments, "new_string", out var newText))
        {
            var lines = new List<(string Color, string Text)>();
            if (ToolArguments.TryReadOptionalBoolean(call.Arguments, "replace_all", out var replaceAll)
                && replaceAll == true)
            {
                lines.Add((Theme.Muted, ReplaceAllNotice));
            }

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

    /// <summary>
    /// One heading per file, then its removed and added lines. The line cap
    /// applies to the whole card, not to each file, so a large patch stays a
    /// card an operator can read.
    /// </summary>
    private static IReadOnlyList<(string Color, string Text)> PatchLines(ToolCall call)
    {
        var lines = new List<(string Color, string Text)>();
        var files = ApplyPatchTool.Preview(call.Arguments);
        var budget = MaximumLines;
        var listed = 0;
        foreach (var file in files)
        {
            if (budget <= 0)
            {
                break;
            }

            lines.Add((Theme.Accent, FileHeading(file)));
            budget--;
            listed++;
            foreach (var removed in file.RemovedLines)
            {
                if (budget <= 0)
                {
                    break;
                }

                lines.Add((Theme.DiffRemoved, '-' + TerminalText.Reveal(removed)));
                budget--;
            }

            foreach (var added in file.AddedLines)
            {
                if (budget <= 0)
                {
                    break;
                }

                lines.Add((Theme.DiffAdded, '+' + TerminalText.Reveal(added)));
                budget--;
            }
        }

        var shown = lines.Count - listed;
        var total = files.Sum(static file => file.RemovedLines.Count + file.AddedLines.Count);
        var hidden = total - shown;
        if (hidden > 0 || listed < files.Count)
        {
            var hiddenFiles = files.Count - listed;
            var more = hiddenFiles > 0
                ? $"... ({hidden} more lines in {files.Count} files)"
                : $"... ({hidden} more lines)";
            lines.Add((Theme.Muted, more));
        }

        return lines;
    }

    private static string FileHeading(PatchFilePreview file)
    {
        var path = TerminalText.Reveal(file.Path);
        return file.Action switch
        {
            "add" => "Add " + path,
            "delete" => "Delete " + path,
            _ when file.MovePath is not null => "Move " + path + " to " + TerminalText.Reveal(file.MovePath),
            _ => "Update " + path
        };
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

            lines.Add((color, prefix + TerminalText.Reveal(line)));
            shown++;
        }

        if (total > MaximumLines)
        {
            lines.Add((Theme.Muted, $"... ({total - MaximumLines} more lines)"));
        }
    }
}
