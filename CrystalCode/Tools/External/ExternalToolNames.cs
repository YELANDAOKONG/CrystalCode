using System.Text.RegularExpressions;

namespace CrystalCode.Tools.External;

/// <summary>
/// Directory and model-facing name rules for external tool sets.
/// </summary>
public static class ExternalToolNames
{
    private static readonly Regex DirectoryPattern = new(
        "^[A-Za-z][A-Za-z0-9._-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ToolPattern = new(
        "^[A-Za-z][A-Za-z0-9_-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        ReadTool.ToolName,
        GlobTool.ToolName,
        GrepTool.ToolName,
        TodoWriteTool.ToolName,
        QuestionTool.ToolName,
        EditTool.ToolName,
        WriteTool.ToolName,
        BashTool.ToolName,
        SkillTool.ToolName
    };

    public static StringComparer OverlayComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static bool IsReserved(string name) =>
        Reserved.Contains(name);

    public static bool IsDirectoryName(string name) =>
        IsMatch(name, DirectoryPattern);

    public static bool IsToolName(string name) =>
        IsMatch(name, ToolPattern) && !IsReserved(name);

    public static bool TryAddRegistered(HashSet<string> names, string name)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (IsReserved(name) || !names.Add(name))
        {
            return false;
        }

        return true;
    }

    private static bool IsMatch(string name, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > ExternalFiles.MaximumNameLength)
        {
            return false;
        }

        try
        {
            return pattern.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
