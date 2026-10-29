namespace CrystalCode.Plugins.Commands;

/// <summary>
/// One part of a <see cref="PluginDocument"/>. The host decides borders,
/// colors, and widths. Text is plain: no markup is interpreted.
/// </summary>
public abstract record PluginBlock
{
    private protected PluginBlock()
    {
    }

    internal abstract void Append(System.Text.StringBuilder text);
}
