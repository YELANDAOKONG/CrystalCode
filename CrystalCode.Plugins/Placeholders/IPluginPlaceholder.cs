namespace CrystalCode.Plugins.Placeholders;

/// <summary>
/// One prompt placeholder a plugin adds. The host calls
/// <see cref="Resolve"/> each time it binds a template. The returned text
/// is inserted as-is.
/// </summary>
public interface IPluginPlaceholder
{
    /// <summary>Gets the placeholder name without braces.</summary>
    string Name { get; }

    /// <summary>Returns the current value for this placeholder.</summary>
    string Resolve(PluginPlaceholderContext context);
}
