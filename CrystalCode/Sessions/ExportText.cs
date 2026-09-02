namespace CrystalCode.Sessions;

/// <summary>
/// Help text for export slash commands.
/// </summary>
public static class ExportText
{
    public static string Usage(string sessionId, string exportDirectory) =>
        "Export\n"
        + "  /export markdown [path] [--system]\n"
        + "  /export json [path] [--system]\n"
        + "  /prompts export [dir]\n\n"
        + "Session  "
        + sessionId
        + "\nDefault directory  "
        + exportDirectory
        + "\n\nMarkdown and JSON omit the live system prompt unless --system is set.\n"
        + "Prompt export writes built-in templates with placeholders.";
}
