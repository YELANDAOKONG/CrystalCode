namespace CrystalCode.Engine.Home;

/// <summary>
/// Which operator extension tree a runtime-data directory belongs to.
/// </summary>
public enum ExtensionDataKind
{
    /// <summary>External tool sets, the <c>tools/</c> tree.</summary>
    Tools,

    /// <summary>Disk plugins, the <c>plugins/</c> tree.</summary>
    Plugins
}
