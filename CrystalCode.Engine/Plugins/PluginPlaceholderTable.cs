using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Models;
using CrystalCode.Plugins.Placeholders;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Placeholders admitted for the current plugin catalog. The environment
/// and model view are empty until the session publishes them.
/// </summary>
public sealed class PluginPlaceholderTable
{
    public static PluginPlaceholderTable Empty { get; } = new([], static _ => { });

    private readonly Dictionary<string, PluginPlaceholderRegistration> _entries;
    private readonly Action<string> _note;
    private IPluginEnvironment _environment = PluginEnvironment.Empty;
    private IPluginModels _models = PluginModels.Empty;

    public PluginPlaceholderTable(
        IEnumerable<PluginPlaceholderRegistration> placeholders,
        Action<string> note)
    {
        ArgumentNullException.ThrowIfNull(placeholders);
        ArgumentNullException.ThrowIfNull(note);
        _note = note;
        _entries = new Dictionary<string, PluginPlaceholderRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var placeholder in placeholders)
        {
            ArgumentNullException.ThrowIfNull(placeholder);
            _entries[placeholder.Name] = placeholder;
        }
    }

    public IPluginEnvironment Environment => _environment;

    public IPluginModels Models => _models;

    public void SetEnvironment(IPluginEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public void SetModels(IPluginModels models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _models = models;
    }

    public bool TryResolve(string name, PluginPlaceholderContext context, out string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(context);
        value = string.Empty;
        if (!_entries.TryGetValue(name, out var entry))
        {
            return false;
        }

        string? resolved;
        try
        {
            resolved = entry.Placeholder.Resolve(context);
        }
        catch (Exception exception)
        {
            _note($"Plugin '{entry.DirectoryName}' placeholder '{entry.Name}' failed: {exception.Message}");
            return false;
        }

        if (resolved is null)
        {
            _note($"Plugin '{entry.DirectoryName}' placeholder '{entry.Name}' returned no text.");
            return false;
        }

        value = resolved;
        return true;
    }
}
