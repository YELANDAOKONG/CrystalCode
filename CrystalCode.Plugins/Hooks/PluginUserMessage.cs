namespace CrystalCode.Plugins.Hooks;

/// <summary>The user message about to be stored.</summary>
public sealed record PluginUserMessage
{
    public PluginUserMessage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }

    public override string ToString() => nameof(PluginUserMessage);
}
