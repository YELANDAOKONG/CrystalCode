using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Built-in tools disabled independently in Plan and Work.
/// </summary>
public sealed record BuiltInToolDisablement
{
    public BuiltInToolDisablement(
        IReadOnlyList<string>? plan = null,
        IReadOnlyList<string>? work = null)
    {
        Plan = BuiltInToolSelection.Normalize(plan);
        Work = BuiltInToolSelection.Normalize(work);
    }

    public IReadOnlyList<string> Plan { get; }

    public IReadOnlyList<string> Work { get; }

    public IReadOnlyList<string> For(bool planMode) => planMode ? Plan : Work;

    public BuiltInToolDisablement With(bool planMode, IReadOnlyList<string> names) =>
        planMode
            ? new BuiltInToolDisablement(names, Work)
            : new BuiltInToolDisablement(Plan, names);

    public static BuiltInToolDisablement Merge(
        BuiltInToolDisablement first,
        BuiltInToolDisablement second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return new BuiltInToolDisablement(
            BuiltInToolSelection.Merge(first.Plan, second.Plan),
            BuiltInToolSelection.Merge(first.Work, second.Work));
    }

    public static BuiltInToolDisablement FromProcessNames(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return new BuiltInToolDisablement();
        }

        var plan = new List<string>();
        var work = new List<string>();
        foreach (var requested in names)
        {
            var value = requested?.Trim() ?? string.Empty;
            var separator = value.IndexOf(':');
            if (separator < 0)
            {
                plan.Add(value);
                work.Add(value);
                continue;
            }

            var mode = value[..separator];
            var name = value[(separator + 1)..];
            if (mode.Equals("plan", StringComparison.OrdinalIgnoreCase))
            {
                plan.Add(name);
            }
            else if (mode.Equals("work", StringComparison.OrdinalIgnoreCase))
            {
                work.Add(name);
            }
            else
            {
                throw new ArgumentException(
                    $"Unknown built-in tool mode '{mode}'. Use plan or work.",
                    nameof(names));
            }
        }

        return new BuiltInToolDisablement(plan, work);
    }

    public IReadOnlyList<string> All() => BuiltInToolSelection.Merge(Plan, Work);
}
