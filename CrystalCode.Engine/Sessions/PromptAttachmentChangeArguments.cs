namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Validates <c>/promptattach</c> arguments.
/// </summary>
public static class PromptAttachmentChangeArguments
{
    public static bool TryParse(
        IReadOnlyList<string> tokens,
        out string action,
        out string name,
        out string error)
    {
        action = string.Empty;
        name = string.Empty;
        error = string.Empty;
        if (tokens.Count != 2)
        {
            error = "Prompt attachments accept enable, disable, up, or down, and one name.";
            return false;
        }

        action = tokens[0].ToLowerInvariant();
        if (action is not ("enable" or "disable" or "up" or "down"))
        {
            error = "Prompt attachments accept enable, disable, up, or down, and one name.";
            return false;
        }

        name = tokens[1];
        return true;
    }
}
