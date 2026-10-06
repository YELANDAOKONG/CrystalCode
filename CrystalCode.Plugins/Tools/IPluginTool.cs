using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Plugins.Tools;

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
}
