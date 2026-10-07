namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Edits the ordered prompt-attachment enable list. Discovery stays outside
/// this type; a name can be disabled even when it is not currently found.
/// </summary>
internal static class PromptAttachmentList
{
    public static bool TryEnable(
        IReadOnlyList<string> current,
        string name,
        bool available,
        out IReadOnlyList<string> next,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        next = current;
        error = string.Empty;
        if (!available)
        {
            error = "Prompt attachment not found  " + name;
            return false;
        }

        if (current.Contains(name, StringComparer.Ordinal))
        {
            error = $"Prompt attachment '{name}' is already enabled.";
            return false;
        }

        next = [.. current, name];
        return true;
    }

    public static bool TryDisable(
        IReadOnlyList<string> current,
        string name,
        out IReadOnlyList<string> next,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        next = current;
        error = string.Empty;
        if (!current.Contains(name, StringComparer.Ordinal))
        {
            error = $"Prompt attachment '{name}' is not enabled.";
            return false;
        }

        next = current.Where(item => !string.Equals(item, name, StringComparison.Ordinal)).ToList();
        return true;
    }

    public static bool TryMove(
        IReadOnlyList<string> current,
        string name,
        bool earlier,
        out IReadOnlyList<string> next,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        next = current;
        error = string.Empty;
        var index = IndexOf(current, name);
        if (index < 0)
        {
            error = $"Prompt attachment '{name}' is not enabled.";
            return false;
        }

        var target = earlier ? index - 1 : index + 1;
        if (target < 0)
        {
            error = $"Prompt attachment '{name}' is already first.";
            return false;
        }

        if (target >= current.Count)
        {
            error = $"Prompt attachment '{name}' is already last.";
            return false;
        }

        var copy = current.ToList();
        (copy[index], copy[target]) = (copy[target], copy[index]);
        next = copy;
        return true;
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
