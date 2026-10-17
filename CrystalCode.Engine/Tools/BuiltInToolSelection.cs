using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Built-in tool names omitted for one process. The set is not saved.
/// </summary>
public static class BuiltInToolSelection
{
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>(requested.Count);
        foreach (var requestedName in requested)
        {
            var name = requestedName?.Trim() ?? string.Empty;
            if (!ExternalToolNames.IsReserved(name))
            {
                throw new ArgumentException(
                    $"Unknown built-in tool '{requestedName}'.",
                    nameof(requested));
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? [] : names.ToArray();
    }

    public static bool Includes(IReadOnlyList<string> names, string toolName)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0 || string.IsNullOrEmpty(toolName))
        {
            return false;
        }

        foreach (var name in names)
        {
            if (string.Equals(name, toolName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string ModelText(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0)
        {
            return string.Empty;
        }

        return "Built-in tools disabled for this process: "
            + string.Join(", ", names)
            + ". Do not call them.";
    }

    public static string OperatorNote(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0)
        {
            return string.Empty;
        }

        return "Disabled built-in tools  " + string.Join(", ", names);
    }
}
