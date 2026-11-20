namespace CrystalCode.Engine.Sessions;

/// <summary>Reported cache writes and request coverage for one lifetime.</summary>
public sealed record ProviderCacheWriteUsage(int LifetimeSeconds, long Tokens, int Reports)
{
    internal static IReadOnlyList<ProviderCacheWriteUsage> Add(
        IReadOnlyList<ProviderCacheWriteUsage> previous, IReadOnlyList<ProviderCacheWriteUsage> reported)
    {
        if (reported.Count == 0)
        {
            return previous;
        }

        var totals = previous.ToDictionary(value => value.LifetimeSeconds);
        foreach (var writes in reported)
        {
            totals.TryGetValue(writes.LifetimeSeconds, out var total);
            totals[writes.LifetimeSeconds] = writes with
            {
                Tokens = (total?.Tokens ?? 0) + writes.Tokens,
                Reports = (total?.Reports ?? 0) + writes.Reports
            };
        }

        return Array.AsReadOnly(totals.Values.OrderBy(value => value.LifetimeSeconds).ToArray());
    }
}
