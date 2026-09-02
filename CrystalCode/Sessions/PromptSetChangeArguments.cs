namespace CrystalCode.Sessions;

/// <summary>
/// Validates <c>/promptset</c> selection arguments.
/// </summary>
public static class PromptSetChangeArguments
{
    public static bool TryParseName(
        IReadOnlyList<string> tokens,
        out string name,
        out string error)
    {
        name = string.Empty;
        error = string.Empty;
        if (tokens.Count > 1)
        {
            error = "Prompt set accepts at most one name.";
            return false;
        }

        name = tokens[0];
        return true;
    }
}
