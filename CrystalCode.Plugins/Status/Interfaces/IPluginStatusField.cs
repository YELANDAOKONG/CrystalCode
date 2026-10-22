namespace CrystalCode.Plugins.Status.Interfaces;
/// <summary>
/// One custom status-line field a plugin adds. The operator opts into it
/// by name. The host does not place it on the default status bar.
/// </summary>
public interface IPluginStatusField
{
    /// <summary>
    /// Gets the field name used in <c>statusLine</c>. A letter, then letters,
    /// digits, or single hyphens, at most 32 characters.
    /// </summary>
    string Name { get; }

    /// <summary>Gets the one-line description shown by <c>/statusline</c>.</summary>
    string Description { get; }
}
