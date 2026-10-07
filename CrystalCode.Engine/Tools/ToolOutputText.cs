using System.Text;

namespace CrystalCode.Engine.Tools;

internal static class ToolOutputText
{
    /// <summary>
    /// Canonical line separator for model-visible tool text. Tool text reaches
    /// providers and session files, so it stays LF on every platform instead of
    /// following Environment.NewLine.
    /// </summary>
    public const char LineSeparator = '\n';

    public static void AppendLine(StringBuilder builder, string line)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(line);
        builder.Append(line);
        builder.Append(LineSeparator);
    }

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
            + LineSeparator
            + $"[truncated to {WorkspaceLimits.MaximumToolOutputCharacters} characters]";
    }
}
