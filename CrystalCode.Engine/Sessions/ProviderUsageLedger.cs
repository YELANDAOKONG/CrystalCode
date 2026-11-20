using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Sessions;

internal sealed class ProviderUsageLedger(string sessionId)
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Scope, string Provider, string Model), ProviderUsageSummary> _summaries = [];

    public string SessionId { get; } = sessionId;

    public void Record(string scope, string provider, string model, ProviderRequestUsage usage)
    {
        lock (_gate)
        {
            var key = (scope, provider, model);
            var hasUsage = usage.InputTokens.HasValue || usage.OutputTokens.HasValue
                || usage.ReasoningTokens.HasValue || usage.CacheReadTokens.HasValue || usage.CacheWriteTokens.HasValue
                || usage.UncachedInputTokens.HasValue || usage.CacheWritesByLifetime is { Count: > 0 };
            if (!_summaries.TryGetValue(key, out var previous))
            {
                previous = new ProviderUsageSummary(scope, provider, model, 0, 0, null, null, null, null, null);
            }

            var coverage = previous.Coverage;
            _summaries[key] = previous with
            {
                HttpAttempts = previous.HttpAttempts + usage.HttpAttempts,
                RequestCount = previous.RequestCount + 1,
                UsageReports = previous.UsageReports + (hasUsage ? 1 : 0),
                InputTokens = AddReported(previous.InputTokens, usage.InputTokens),
                OutputTokens = AddReported(previous.OutputTokens, usage.OutputTokens),
                ReasoningTokens = AddReported(previous.ReasoningTokens, usage.ReasoningTokens),
                CacheReadTokens = AddReported(previous.CacheReadTokens, usage.CacheReadTokens),
                CacheWriteTokens = AddReported(previous.CacheWriteTokens, usage.CacheWriteTokens),
                UncachedInputTokens = AddReported(previous.UncachedInputTokens, usage.UncachedInputTokens),
                Coverage = new ProviderUsageCoverage(
                    coverage.InputTokens + Reported(usage.InputTokens),
                    coverage.OutputTokens + Reported(usage.OutputTokens),
                    coverage.ReasoningTokens + Reported(usage.ReasoningTokens),
                    coverage.CacheReadTokens + Reported(usage.CacheReadTokens),
                    coverage.CacheWriteTokens + Reported(usage.CacheWriteTokens),
                    coverage.UncachedInputTokens + Reported(usage.UncachedInputTokens)),
                CacheWritesByLifetime = AddCacheWrites(previous.CacheWritesByLifetime, usage)
            };
        }
    }

    public IReadOnlyList<ProviderUsageSummary> Snapshot()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_summaries.Values
                .OrderBy(static value => value.Scope, StringComparer.Ordinal)
                .ThenBy(static value => value.Provider, StringComparer.Ordinal)
                .ThenBy(static value => value.Model, StringComparer.Ordinal)
                .ToArray());
        }
    }

    private static long? AddReported(long? previous, long? next) =>
        previous is null ? next : next is null ? previous : previous.Value + next.Value;

    private static int Reported(long? count) => count.HasValue ? 1 : 0;

    private static IReadOnlyList<ProviderCacheWriteUsage> AddCacheWrites(
        IReadOnlyList<ProviderCacheWriteUsage> previous, ProviderRequestUsage usage)
    {
        if (usage.CacheWritesByLifetime is not { Count: > 0 } reported)
        {
            return previous;
        }

        var totals = previous.ToDictionary(value => value.LifetimeSeconds);
        foreach (var (lifetime, tokens) in reported)
        {
            totals.TryGetValue(lifetime, out var total);
            totals[lifetime] = new ProviderCacheWriteUsage(
                lifetime, (total?.Tokens ?? 0) + tokens, (total?.Reports ?? 0) + 1);
        }

        return Array.AsReadOnly(totals.Values.OrderBy(value => value.LifetimeSeconds).ToArray());
    }
}
