namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// One item in an outbound model request. <see cref="Id"/> is assigned by the
/// host. An ordinary hook must reuse ids from the request it received. An
/// <see cref="IPluginRawHook"/> may also return an item with a new id.
/// </summary>
public abstract record PluginModelItem
{
    private protected PluginModelItem(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id.Trim();
    }

    public string Id { get; }

    public override string ToString() => nameof(PluginModelItem);
}
