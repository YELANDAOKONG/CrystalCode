using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Transcript;

/// <summary>
/// Chooses how tool result blocks render in the transcript viewport.
/// </summary>
internal static class TranscriptResultDisplay
{
    internal const string BashToolName = "bash";

    internal const string EditToolName = "edit";

    internal const string WriteToolName = "write";

    internal const string ApplyPatchToolName = "apply_patch";

    public static bool ShouldRender(
        TranscriptKind kind,
        string? toolName,
        bool verboseTools,
        bool verboseCommands,
        bool verboseResults)
    {
        if (kind == TranscriptKind.Error)
        {
            return true;
        }

        if (kind != TranscriptKind.Result)
        {
            return true;
        }

        if (!verboseResults)
        {
            return false;
        }

        if (IsEditOrWrite(toolName))
        {
            return true;
        }

        if (IsCommand(toolName))
        {
            return true;
        }

        return verboseTools;
    }

    public static string Text(
        TranscriptKind kind,
        string text,
        string? toolName,
        bool verboseTools,
        bool verboseCommands)
    {
        if (kind == TranscriptKind.Error || IsEditOrWrite(toolName))
        {
            return text;
        }

        if (IsCommand(toolName))
        {
            return verboseCommands ? text : ToolResultText.CompactCommandBody(text);
        }

        return verboseTools ? text : string.Empty;
    }

    private static bool IsCommand(string? toolName) =>
        // Background task tools carry command output. They follow the command
        // switch rather than disappearing with read and search results.
        string.Equals(toolName, BashToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "bash_start", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "bash_status", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "bash_logs", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, "bash_stop", StringComparison.OrdinalIgnoreCase);

    private static bool IsEditOrWrite(string? toolName) =>
        string.Equals(toolName, EditToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, WriteToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, ApplyPatchToolName, StringComparison.OrdinalIgnoreCase);
}
