using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Host-owned session directory list for <c>{{directories}}</c>.
/// Empty when the session has no added rules. Not an overlay file.
/// </summary>
public static class DirectoryAccessText
{
    public static string Render(IReadOnlyList<DirectoryAccess> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
        {
            return string.Empty;
        }

        return "Session directory access (most-specific directory rule wins):\n"
            + string.Join('\n', rules.Select(rule => $"- {rule.Mode}: {rule.Path}"))
            + "\nRead permits built-in file reads and searches; write includes read and follows the approval mode."
            + " Explicit read-only directories cannot be edited or written, even after approval."
            + " Unlisted outside paths still require approval. Relative paths remain workspace-relative."
            + " Search only the requested root, not all accessible directories."
            + " These rules do not sandbox shell or extension processes; do not use them to bypass directory access."
            + " Only the operator can change access with /access.";
    }
}
