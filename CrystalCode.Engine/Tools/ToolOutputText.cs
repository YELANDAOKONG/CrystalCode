namespace CrystalCode.Engine.Tools;

internal static class ToolOutputText
{
    public static string Timeout(int seconds) =>
        $"The command timed out after {seconds} {(seconds == 1 ? "second" : "seconds")}.";

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
