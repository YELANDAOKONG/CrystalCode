using System.Text.RegularExpressions;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Expands <c>${NAME}</c> references from the process environment so
/// project files can point at secrets without containing them.
/// </summary>
internal static class McpEnvironmentText
{
    private static readonly Regex Reference = new(
        @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Returns false and names the first variable that is not set. An empty
    /// value counts as set.
    /// </summary>
    public static bool TryExpand(
        string text,
        Func<string, string?> lookup,
        out string expanded,
        out string missing)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lookup);
        var absent = string.Empty;
        var result = Reference.Replace(text, match =>
        {
            var value = lookup(match.Groups[1].Value);
            if (value is not null)
            {
                return value;
            }

            if (absent.Length == 0)
            {
                absent = match.Groups[1].Value;
            }

            return match.Value;
        });
        expanded = result;
        missing = absent;
        return absent.Length == 0;
    }
}
