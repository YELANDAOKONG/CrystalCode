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

    public static bool ShouldRender(
        TranscriptKind kind,
        string? toolName,
        bool verboseTools,
        bool verboseCommands)
    {
        if (kind == TranscriptKind.Error)
        {
            return true;
        }

        if (kind != TranscriptKind.Result)
        {
            return true;
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
        string.Equals(toolName, BashToolName, StringComparison.OrdinalIgnoreCase);

    private static bool IsEditOrWrite(string? toolName) =>
        string.Equals(toolName, EditToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, WriteToolName, StringComparison.OrdinalIgnoreCase);
}
