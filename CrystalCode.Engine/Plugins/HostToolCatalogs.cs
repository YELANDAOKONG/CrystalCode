namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Which built-in tool catalogs a contribution joins.
/// A tool joins at least one.
/// </summary>
public sealed class HostToolCatalogs
{
    private readonly HashSet<HostToolCatalog> _catalogs;

    public static HostToolCatalogs Plan { get; } = new(HostToolCatalog.Plan);

    public static HostToolCatalogs Work { get; } = new(HostToolCatalog.Work);

    public static HostToolCatalogs PlanAndWork { get; } = new(HostToolCatalog.Plan, HostToolCatalog.Work);

    public HostToolCatalogs(params HostToolCatalog[] catalogs)
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
                "A tool must belong to at least one catalog.",
                nameof(catalogs));
        }
    }

    public bool Contains(HostToolCatalog catalog) => _catalogs.Contains(catalog);

    public override string ToString() => nameof(HostToolCatalogs);
}
