using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Built-in tools that disk plugins replace, per catalog. A replacement keeps
/// the model-facing name, so approval classifies it as the built-in.
/// </summary>
public sealed class PluginReplacements
{
    private readonly Dictionary<string, ITool> _plan = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITool> _work = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMultimodalTool?> _planMultimodal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMultimodalTool?> _workMultimodal = new(StringComparer.Ordinal);

    public static PluginReplacements Empty { get; } = new();

    public bool IsEmpty => _plan.Count == 0 && _work.Count == 0;

    internal bool TryAdd(
        HostToolCatalog catalog,
        string name,
        ITool tool,
        IMultimodalTool? multimodal)
    {
        var (text, images) = Select(catalog);
        if (!text.TryAdd(name, tool))
        {
            return false;
        }

        images[name] = multimodal;
        return true;
    }

    /// <summary>
    /// Swaps each replaced tool that is present. A name the catalog does not
    /// hold stays absent, so a replacement cannot add a tool the session left out.
    /// </summary>
    public IReadOnlyList<ITool> Apply(IReadOnlyList<ITool> tools, HostToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var (text, _) = Select(catalog);
        if (text.Count == 0)
        {
            return tools;
        }

        return [.. tools.Select(tool =>
            text.TryGetValue(tool.Definition.Name, out var replacement) ? replacement : tool)];
    }

    /// <summary>
    /// Swaps the image variant of each replaced tool that is present. A
    /// replaced tool with no image variant of its own loses the built-in one.
    /// </summary>
    public IReadOnlyList<IMultimodalTool> Apply(
        IReadOnlyList<IMultimodalTool> tools,
        HostToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var (_, images) = Select(catalog);
        if (images.Count == 0)
        {
            return tools;
        }

        var result = new List<IMultimodalTool>();
        foreach (var tool in tools)
        {
            if (!images.TryGetValue(tool.Definition.Name, out var replacement))
            {
                result.Add(tool);
            }
            else if (replacement is not null)
            {
                result.Add(replacement);
            }
        }

        return result;
    }

    private (Dictionary<string, ITool> Text, Dictionary<string, IMultimodalTool?> Images) Select(
        HostToolCatalog catalog) =>
        catalog switch
        {
            HostToolCatalog.Plan => (_plan, _planMultimodal),
            HostToolCatalog.Work => (_work, _workMultimodal),
            _ => throw new ArgumentOutOfRangeException(nameof(catalog))
        };
}
