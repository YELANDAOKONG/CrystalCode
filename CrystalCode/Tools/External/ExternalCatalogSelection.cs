namespace CrystalCode.Tools.External;

/// <summary>
/// Plan and Work catalog membership for one external tool.
/// </summary>
public sealed record ExternalCatalogSelection
{
    public static ExternalCatalogSelection Both { get; } = new(true, true);

    public ExternalCatalogSelection(bool plan, bool work)
    {
        if (!plan && !work)
        {
            throw new ArgumentException(
                "An external tool must belong to Plan, Work, or both.");
        }

        Plan = plan;
        Work = work;
    }

    public bool Plan { get; }

    public bool Work { get; }

    public static bool TryParse(IReadOnlyList<string>? values, out ExternalCatalogSelection selection)
    {
        selection = Both;
        if (values is null)
        {
            return true;
        }

        if (values.Count == 0)
        {
            return false;
        }

        var plan = false;
        var work = false;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var key = value.Trim().ToLowerInvariant();
            switch (key)
            {
                case "plan":
                    plan = true;
                    break;
                case "work":
                    work = true;
                    break;
                default:
                    return false;
            }
        }

        if (!plan && !work)
        {
            return false;
        }

        selection = new ExternalCatalogSelection(plan, work);
        return true;
    }

    public override string ToString() => nameof(ExternalCatalogSelection);
}
