namespace CrystalCode.Engine.Sessions;

/// <summary>Provider-reported prompt cache input, reads, writes, misses, and their coverage.</summary>
public sealed record SessionCacheUsage
{
    public long RequestCount { get; init; }
    public long? InputTokens { get; init; }
    public long? ReadTokens { get; init; }
    public long? WriteTokens { get; init; }
    public long? MissTokens { get; init; }
    public long ReadReports { get; init; }
    public long WriteReports { get; init; }
    public long InputReports { get; init; }
    public long MissReports { get; init; }
    public long ComparableRequests { get; init; }
    public long ComparableInputTokens { get; init; }
    public long ComparableReadTokens { get; init; }
    public IReadOnlyList<ProviderCacheWriteUsage> WritesByLifetime { get; init; } = [];

    /// <summary>The same totals per request scope. Requests saved before scopes were recorded are absent.</summary>
    public IReadOnlyList<SessionCacheScopeUsage> ByScope { get; init; } = [];

    public decimal? GetReadRatePercent() => ComparableInputTokens > 0
        ? 100m * ComparableReadTokens / ComparableInputTokens
        : null;

    public decimal? GetMissRatePercent() => ComparableInputTokens > 0
        ? 100m - GetReadRatePercent()!.Value
        : null;

    public long? GetMissTokens() => MissTokens
        ?? (ComparableInputTokens > 0 ? ComparableInputTokens - ComparableReadTokens : null);

    internal SessionCacheUsage Add(SessionCacheUsageSample usage)
    {
        var comparable = usage.InputTokens is > 0 && usage.ReadTokens is >= 0
            && usage.ReadTokens <= usage.InputTokens;
        var single = new SessionCacheUsage
        {
            RequestCount = 1,
            InputTokens = usage.InputTokens,
            ReadTokens = usage.ReadTokens,
            WriteTokens = usage.WriteTokens,
            MissTokens = usage.UncachedInputTokens
                ?? (comparable ? usage.InputTokens.GetValueOrDefault() - usage.ReadTokens.GetValueOrDefault() : null),
            ReadReports = usage.ReadTokens.HasValue ? 1 : 0,
            WriteReports = usage.WriteTokens.HasValue ? 1 : 0,
            InputReports = usage.InputTokens.HasValue ? 1 : 0,
            MissReports = usage.UncachedInputTokens.HasValue || comparable ? 1 : 0,
            ComparableRequests = comparable ? 1 : 0,
            ComparableInputTokens = comparable ? usage.InputTokens.GetValueOrDefault() : 0,
            ComparableReadTokens = comparable ? usage.ReadTokens.GetValueOrDefault() : 0,
            WritesByLifetime = usage.WritesByLifetime ?? []
        };
        return Add(usage.Scope is null ? single : single with { ByScope = [new SessionCacheScopeUsage(usage.Scope, single)] });
    }

    internal SessionCacheUsage Add(SessionCacheUsage usage) => this with
    {
        RequestCount = RequestCount + usage.RequestCount,
        InputTokens = AddReported(InputTokens, usage.InputTokens),
        ReadTokens = AddReported(ReadTokens, usage.ReadTokens),
        WriteTokens = AddReported(WriteTokens, usage.WriteTokens),
        MissTokens = AddReported(MissTokens, usage.MissTokens),
        ReadReports = ReadReports + usage.ReadReports,
        WriteReports = WriteReports + usage.WriteReports,
        InputReports = InputReports + usage.InputReports,
        MissReports = MissReports + usage.MissReports,
        ComparableRequests = ComparableRequests + usage.ComparableRequests,
        ComparableInputTokens = ComparableInputTokens + usage.ComparableInputTokens,
        ComparableReadTokens = ComparableReadTokens + usage.ComparableReadTokens,
        WritesByLifetime = ProviderCacheWriteUsage.Add(WritesByLifetime, usage.WritesByLifetime),
        ByScope = SessionCacheScopeUsage.Add(ByScope, usage.ByScope)
    };

    private static long? AddReported(long? total, long? added) =>
        added is null ? total : (total ?? 0) + added.Value;
}
