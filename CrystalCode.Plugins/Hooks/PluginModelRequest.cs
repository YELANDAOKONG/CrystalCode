namespace CrystalCode.Plugins.Hooks;

/// <summary>The outbound items a model hook may project for one call.</summary>
public sealed record PluginModelRequest
{
    public PluginModelRequest(
        PluginModelPurpose purpose,
        IEnumerable<PluginModelItem> items,
        bool acceptsImages = false)
    {
        ArgumentNullException.ThrowIfNull(items);
        Purpose = purpose;
        Items = [.. items];
        AcceptsImages = acceptsImages;
    }

    public PluginModelPurpose Purpose { get; }

    public IReadOnlyList<PluginModelItem> Items { get; }

    /// <summary>
    /// True when this call sends images to the model. Text-only calls strip
    /// image markers, so an image added to them would never reach the model.
    /// </summary>
    public bool AcceptsImages { get; }

    public override string ToString() => nameof(PluginModelRequest);
}
