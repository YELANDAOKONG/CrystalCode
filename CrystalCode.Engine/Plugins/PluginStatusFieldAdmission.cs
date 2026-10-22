using CrystalCode.Engine.Configuration;
using CrystalCode.Plugins.Status;
using CrystalCode.Plugins.Status.Interfaces;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Accepts plugin status-field names that the host does not already own.
/// The first accepted name wins.
/// </summary>
public static class PluginStatusFieldAdmission
{
    private static readonly string[] CommandWords = ["on", "off", "reset"];

    public static bool TryAdmit(
        string directoryName,
        IPluginStatusField? field,
        ISet<string> accepted,
        ICollection<string> notes,
        out PluginStatusFieldRegistration? registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(notes);
        registration = null;
        if (field is null)
        {
            notes.Add($"Plugin '{directoryName}' omitted a missing status field.");
            return false;
        }

        string rawName;
        try
        {
            rawName = field.Name ?? string.Empty;
        }
        catch (Exception exception)
        {
            notes.Add($"Plugin '{directoryName}' omitted a status field: {exception.Message}");
            return false;
        }

        var name = rawName.Trim();
        if (!PluginStatusFieldNames.IsName(name))
        {
            notes.Add($"Plugin '{directoryName}' omitted status field '{name}'.");
            return false;
        }

        if (Owns(name))
        {
            notes.Add(
                $"Plugin '{directoryName}' omitted status field '{name}' because the host owns that name.");
            return false;
        }

        var stored = name.ToLowerInvariant();
        if (!accepted.Add(stored))
        {
            notes.Add($"Plugin '{directoryName}' omitted duplicate status field '{name}'.");
            return false;
        }

        string description;
        try
        {
            description = PluginStatusText.Normalize(
                field.Description,
                PluginStatusText.MaximumDescriptionLength);
        }
        catch (Exception exception)
        {
            accepted.Remove(stored);
            notes.Add($"Plugin '{directoryName}' omitted status field '{name}': {exception.Message}");
            return false;
        }

        registration = new PluginStatusFieldRegistration(
            stored,
            directoryName,
            description,
            field);
        return true;
    }

    private static bool Owns(string name)
    {
        foreach (var owned in StatusLineSettings.AvailableFields)
        {
            if (string.Equals(owned, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var owned in CommandWords)
        {
            if (string.Equals(owned, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
