namespace CrystalCode.Engine.Sessions;

/// <summary>Cache usage for requests of one scope: Main, Child, or Auxiliary.</summary>
public sealed record SessionCacheScopeUsage(string Scope, SessionCacheUsage Usage)
{
    internal static IReadOnlyList<SessionCacheScopeUsage> Add(
        IReadOnlyList<SessionCacheScopeUsage> previous, IReadOnlyList<SessionCacheScopeUsage> added)
    {
        if (added.Count == 0)
        {
            return previous;
        }

        var merged = previous.ToDictionary(static value => value.Scope, static value => value.Usage, StringComparer.Ordinal);
        foreach (var value in added)
        {
            merged[value.Scope] = merged.TryGetValue(value.Scope, out var existing) ? existing.Add(value.Usage) : value.Usage;
        }

        return Array.AsReadOnly(merged
            .OrderBy(static value => Rank(value.Key))
            .ThenBy(static value => value.Key, StringComparer.Ordinal)
            .Select(static value => new SessionCacheScopeUsage(value.Key, value.Value))
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
