using System.Text.RegularExpressions;

using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Server names and the model-facing names of the tools they provide.
/// </summary>
public static class McpServerNames
{
    public const string ToolPrefix = "mcp__";

    public const string Separator = "__";

    public const int MaximumServerNameLength = 32;

    private static readonly Regex ServerPattern = new(
        "^[A-Za-z][A-Za-z0-9_-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex InvalidToolCharacters = new(
        "[^A-Za-z0-9_-]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// A server name becomes part of every tool name. It cannot contain the
    /// separator or end with an underscore, or the tool name would be ambiguous.
    /// </summary>
    public static bool IsServerName(string name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaximumServerNameLength
        && !name.Contains(Separator, StringComparison.Ordinal)
        && !name.EndsWith('_')
        && ServerPattern.IsMatch(name);

    /// <summary>
    /// Builds <c>mcp__server__tool</c>. Characters providers reject become
    /// underscores. Returns false when the tool name is empty or the result
    /// exceeds the model-facing name limit.
    /// </summary>
    public static bool TryToolName(string serverName, string toolName, out string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(toolName)
            || toolName.Length > ExternalFiles.MaximumNameLength)
        {
            return false;
        }

        var candidate = ToolPrefix
            + serverName
            + Separator
            + InvalidToolCharacters.Replace(toolName, "_");
        if (candidate.Length > ExternalFiles.MaximumNameLength)
        {
            return false;
        }

        name = candidate;
        return true;
    }
}
