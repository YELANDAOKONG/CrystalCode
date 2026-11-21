namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Cache usage for the requests of one scope, provider, and model. Views group
/// these rows further; the stored samples keep the full detail.
/// </summary>
public sealed record SessionCacheBreakdown(string Scope, string? Provider, string? Model, SessionCacheUsage Usage)
{
    /// <summary>Groups samples that recorded a scope. Samples saved before scopes existed are left out.</summary>
    internal static IReadOnlyList<SessionCacheBreakdown> From(IEnumerable<SessionCacheUsageSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return Array.AsReadOnly(samples
            .Where(static sample => sample.Scope is not null)
            .GroupBy(static sample => (sample.Scope!, sample.Provider, sample.Model))
            .Select(static group => new SessionCacheBreakdown(
                group.Key.Item1,
                group.Key.Provider,
                group.Key.Model,
                group.Aggregate(new SessionCacheUsage(), static (total, sample) => total.Add(sample))))
            .OrderBy(static row => Rank(row.Scope))
            .ThenBy(static row => row.Scope, StringComparer.Ordinal)
            .ThenBy(static row => row.Provider, StringComparer.Ordinal)
            .ThenBy(static row => row.Model, StringComparer.Ordinal)
            .ToArray());
    }

    private static int Rank(string scope) => scope switch
    {
        "Main" => 0,
        "Child" => 1,
        "Auxiliary" => 2,
        _ => 3
    };
}
