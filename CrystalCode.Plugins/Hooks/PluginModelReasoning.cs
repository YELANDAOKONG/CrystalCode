namespace CrystalCode.Plugins.Hooks;

/// <summary>Reasoning text already present in an outbound model request.</summary>
public sealed record PluginModelReasoning : PluginModelItem
{
    public PluginModelReasoning(string id, string text)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }

    public override string ToString() => nameof(PluginModelReasoning);
}
