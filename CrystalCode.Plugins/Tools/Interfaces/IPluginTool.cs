using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Plugins.Tools.Interfaces;

/// <summary>
/// A tool contributed by a disk plugin. The instance is created by the plugin.
/// Host facts arrive per call through <c>IHostTool</c> when the tool implements it.
/// </summary>
public interface IPluginTool
{
    /// <summary>Gets the model-facing name. It must match <see cref="ITool.Definition"/>.</summary>
    string Name { get; }

    /// <summary>Gets the catalogs that register this tool.</summary>
    PluginToolCatalogs Catalogs { get; }

    /// <summary>Gets the text tool.</summary>
    ITool Tool { get; }

    /// <summary>
    /// Gets an optional image-capable tool with the same name, when this
    /// contribution also returns images.
    /// </summary>
    IMultimodalTool? Multimodal => null;

    /// <summary>
    /// Gets whether this tool replaces the built-in tool of the same name in
    /// its catalogs. The host still classifies and approves the call by that
    /// name, so the plugin is trusted not to do more than the built-in does.
    /// Without this flag a built-in name is refused.
    /// </summary>
    bool ReplacesBuiltIn => false;

    /// <summary>
    /// Gets whether the host may run this tool without a prompt or review when
    /// a plugin classifier rates the call Read risk with Network authority.
    /// The host honors this only for plugins installed in Home, never for a
    /// project plugin, and never for a call with any other classification,
    /// a forced prompt, or a built-in replacement. Default: false.
    /// </summary>
    bool AutoApprovesReadOnlyNetwork => false;
}
