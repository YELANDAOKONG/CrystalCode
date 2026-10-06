namespace CrystalCode.Plugins.Tools;

/// <summary>
/// Which tool catalogs a disk plugin tool joins.
/// A tool joins at least one.
/// </summary>
public sealed class PluginToolCatalogs
{
    private readonly HashSet<PluginToolCatalog> _catalogs;

    public static PluginToolCatalogs Plan { get; } = new(PluginToolCatalog.Plan);

    public static PluginToolCatalogs Work { get; } = new(PluginToolCatalog.Work);

    public static PluginToolCatalogs PlanAndWork { get; } = new(
        PluginToolCatalog.Plan,
        PluginToolCatalog.Work);

    public PluginToolCatalogs(params PluginToolCatalog[] catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        _catalogs = [];
        foreach (var catalog in catalogs)
        {
            if (!Enum.IsDefined(catalog))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(catalogs),
                    catalog,
                    "Tool catalog is not defined.");
            }

            _catalogs.Add(catalog);
        }

        if (_catalogs.Count == 0)
        {
            throw new ArgumentException(
                "A plugin tool must belong to at least one catalog.",
                nameof(catalogs));
        }
    }

    public bool Contains(PluginToolCatalog catalog) => _catalogs.Contains(catalog);

    public override string ToString() => nameof(PluginToolCatalogs);
}
