using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Sessions;

/// <summary>Cache token totals and their coverage for tracked provider requests.</summary>
public sealed record SessionCacheUsage
{
    public long RequestCount { get; init; }
    public long? ReadTokens { get; init; }
    public long? WriteTokens { get; init; }
    public long ReadReports { get; init; }
    public long WriteReports { get; init; }
    public long ComparableRequests { get; init; }
    public long ComparableInputTokens { get; init; }
    public long ComparableReadTokens { get; init; }
    public IReadOnlyList<ProviderCacheWriteUsage> WritesByLifetime { get; init; } = [];

    internal SessionCacheUsage Add(ProviderRequestUsage usage)
    {
        var comparable = usage.InputTokens is > 0 && usage.CacheReadTokens is >= 0
            && usage.CacheReadTokens <= usage.InputTokens;
        return Add(new SessionCacheUsage
        {
            RequestCount = 1,
            ReadTokens = usage.CacheReadTokens,
            WriteTokens = usage.CacheWriteTokens,
            ReadReports = usage.CacheReadTokens.HasValue ? 1 : 0,
            WriteReports = usage.CacheWriteTokens.HasValue ? 1 : 0,
            ComparableRequests = comparable ? 1 : 0,
            ComparableInputTokens = comparable ? usage.InputTokens.GetValueOrDefault() : 0,
            ComparableReadTokens = comparable ? usage.CacheReadTokens.GetValueOrDefault() : 0,
            WritesByLifetime = usage.CacheWritesByLifetime is { Count: > 0 } writes
                ? writes.Select(value => new ProviderCacheWriteUsage(value.Key, value.Value, 1)).ToArray()
                : []
        });
    }

    internal SessionCacheUsage Add(SessionCacheUsage usage) => this with
    {
        RequestCount = RequestCount + usage.RequestCount,
        ReadTokens = AddReported(ReadTokens, usage.ReadTokens),
        WriteTokens = AddReported(WriteTokens, usage.WriteTokens),
        ReadReports = ReadReports + usage.ReadReports,
        WriteReports = WriteReports + usage.WriteReports,
        ComparableRequests = ComparableRequests + usage.ComparableRequests,
        ComparableInputTokens = ComparableInputTokens + usage.ComparableInputTokens,
        ComparableReadTokens = ComparableReadTokens + usage.ComparableReadTokens,
        WritesByLifetime = ProviderCacheWriteUsage.Add(WritesByLifetime, usage.WritesByLifetime)
    };

    private static long? AddReported(long? total, long? added) =>
        added is null ? total : (total ?? 0) + added.Value;
}
