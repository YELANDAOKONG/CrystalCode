namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// Compaction text a hook may extend. Returned text is appended.
/// The hook cannot drop history or replace the compactor.
/// </summary>
public sealed record PluginCompaction
{
    public PluginCompaction(PluginCompactionPhase phase, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Phase = phase;
        Text = text;
    }

    public PluginCompactionPhase Phase { get; }

    public string Text { get; }

    public override string ToString() => nameof(PluginCompaction);
}
