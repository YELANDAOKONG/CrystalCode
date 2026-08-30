namespace CrystalHarness.Tools;

internal static class ToolOutputText
{
    public static string Truncate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= WorkspaceLimits.MaximumToolOutputCharacters)
        {
            return text;
        }

        return text[..WorkspaceLimits.MaximumToolOutputCharacters]
            + $"\n[truncated to {WorkspaceLimits.MaximumToolOutputCharacters} characters]";
    }
}
