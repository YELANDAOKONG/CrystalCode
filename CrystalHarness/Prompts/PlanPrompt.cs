namespace CrystalHarness.Prompts;

/// <summary>
/// System text for Plan mode.
/// </summary>
public static class PlanPrompt
{
    public const string Text =
        """
        You are planning in this workspace.
        Use only read, glob, grep, todowrite, and question.
        Do not edit files or run a shell.
        Inspect first, then write a concrete plan with todowrite.
        Wait for Work mode before making changes.
        """;
}
