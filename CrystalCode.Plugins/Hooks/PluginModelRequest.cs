namespace CrystalCode.Plugins.Hooks;

/// <summary>The outbound items a model hook may project for one call.</summary>
public sealed record PluginModelRequest
{
    public PluginModelRequest(PluginModelPurpose purpose, IEnumerable<PluginModelItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Purpose = purpose;
        Items = [.. items];
    }

    public PluginModelPurpose Purpose { get; }

    public IReadOnlyList<PluginModelItem> Items { get; }

    public override string ToString() => nameof(PluginModelRequest);
}
