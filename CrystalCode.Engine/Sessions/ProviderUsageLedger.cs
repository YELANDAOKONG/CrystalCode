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
                || usage.ReasoningTokens.HasValue || usage.CacheReadTokens.HasValue || usage.CacheWriteTokens.HasValue;
            if (!_summaries.TryGetValue(key, out var previous))
            {
                _summaries[key] = new ProviderUsageSummary(
                    scope, provider, model, usage.HttpAttempts, hasUsage ? 1 : 0,
                    usage.InputTokens, usage.OutputTokens, usage.ReasoningTokens,
                    usage.CacheReadTokens, usage.CacheWriteTokens);
                return;
            }

            _summaries[key] = previous with
            {
                HttpAttempts = previous.HttpAttempts + usage.HttpAttempts,
                UsageReports = previous.UsageReports + (hasUsage ? 1 : 0),
                InputTokens = AddReported(previous.InputTokens, usage.InputTokens),
                OutputTokens = AddReported(previous.OutputTokens, usage.OutputTokens),
                ReasoningTokens = AddReported(previous.ReasoningTokens, usage.ReasoningTokens),
                CacheReadTokens = AddReported(previous.CacheReadTokens, usage.CacheReadTokens),
                CacheWriteTokens = AddReported(previous.CacheWriteTokens, usage.CacheWriteTokens)
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
}
