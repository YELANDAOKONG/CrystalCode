namespace CrystalCode.Plugins.Commands;

/// <summary>One labelled value in a <see cref="PluginKeyValues"/> block.</summary>
public sealed record PluginKeyValue
{
    public PluginKeyValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = PluginDocumentLimits.Clip(key);
        Value = PluginDocumentLimits.Clip(value);
    }

    public string Key { get; }

    public string Value { get; }
}
