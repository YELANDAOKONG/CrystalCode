namespace CrystalHarness.Prompts;

/// <summary>
/// Caller-authored text for context-summary generation.
/// </summary>
public static class CompactionPrompt
{
    public const string Marker = "## Earlier context";

    public const string SystemText =
        """
        You summarize discarded tool output for a coding agent.
        Keep file paths, errors, test results, and decisions.
        Do not invent facts. Do not include secrets or credentials.
        Reply with a short English summary and no preamble.
        """;

    public static string UserText(string excerpt, string todos)
    {
        ArgumentNullException.ThrowIfNull(excerpt);
        ArgumentNullException.ThrowIfNull(todos);
        var text = "Summarize this discarded tool output:\n\n" + excerpt.Trim();
        if (todos.Trim().Length > 0 && todos.Trim() != "No todos.")
        {
            text += "\n\nOpen todos to preserve:\n" + todos.Trim();
        }

        return text;
    }
}
