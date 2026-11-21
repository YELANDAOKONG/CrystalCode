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

    internal SessionCacheUsage Add(SessionCacheUsageSample usage)
    {
        var comparable = usage.InputTokens is > 0 && usage.ReadTokens is >= 0
            && usage.ReadTokens <= usage.InputTokens;
        return Add(new SessionCacheUsage
        {
            RequestCount = 1,
            ReadTokens = usage.ReadTokens,
            WriteTokens = usage.WriteTokens,
            ReadReports = usage.ReadTokens.HasValue ? 1 : 0,
            WriteReports = usage.WriteTokens.HasValue ? 1 : 0,
            ComparableRequests = comparable ? 1 : 0,
            ComparableInputTokens = comparable ? usage.InputTokens.GetValueOrDefault() : 0,
            ComparableReadTokens = comparable ? usage.ReadTokens.GetValueOrDefault() : 0,
            WritesByLifetime = usage.WritesByLifetime ?? []
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
