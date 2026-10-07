using CrystalCode.Engine.Prompts;
using CrystalCode.Plugins.Placeholders;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Accepts plugin placeholder names that the host does not already own.
/// The first accepted name wins.
/// </summary>
public static class PluginPlaceholderAdmission
{
    public static bool TryAdmit(
        string directoryName,
        IPluginPlaceholder? placeholder,
        ISet<string> accepted,
        ICollection<string> notes,
        out PluginPlaceholderRegistration? registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(notes);
        registration = null;
        if (placeholder is null)
        {
            notes.Add($"Plugin '{directoryName}' omitted a missing placeholder.");
            return false;
        }

        string rawName;
        try
        {
            rawName = placeholder.Name ?? string.Empty;
        }
        catch (Exception exception)
        {
            notes.Add($"Plugin '{directoryName}' omitted a placeholder: {exception.Message}");
            return false;
        }

        var name = rawName.Trim();
        if (!PluginPlaceholderNames.IsName(name))
        {
            notes.Add($"Plugin '{directoryName}' omitted placeholder '{rawName.Trim()}'.");
            return false;
        }

        if (Owns(name))
        {
            notes.Add(
                $"Plugin '{directoryName}' omitted placeholder '{name}' because the host owns that name.");
            return false;
        }

        if (!accepted.Add(name))
        {
            notes.Add($"Plugin '{directoryName}' omitted duplicate placeholder '{name}'.");
            return false;
        }

        registration = new PluginPlaceholderRegistration(name.ToLowerInvariant(), directoryName, placeholder);
        return true;
    }

    private static bool Owns(string name)
    {
        foreach (var owned in PromptPlaceholder.All)
        {
            if (string.Equals(owned, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
