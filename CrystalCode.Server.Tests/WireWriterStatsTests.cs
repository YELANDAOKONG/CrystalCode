using System.Text.Json;

using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Wire;

using Xunit;

namespace CrystalCode.Server.Tests;

public sealed class WireWriterStatsTests
{
    [Fact]
    public void Stats_WritesUnknownTokensAsNullAndKeepsExplicitZero()
    {
        var report = new SessionStatsReport(1, 1, 1, 0, null, null, 0, null, null, null, 0, null, null, [])
        {
            TokenTrackedSessions = 0,
            ReasoningTrackedSessions = 1,
            CacheTrackedSessions = 1,
            CacheWindowDays = 7,
            CacheUsage = new SessionCacheUsage { RequestCount = 1, ReadReports = 1, ReadTokens = 0 }
        };

        var written = Write(report);

        Assert.Equal(JsonValueKind.Null, written.GetProperty("inputTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, written.GetProperty("totalTokens").ValueKind);
        Assert.Equal(0, written.GetProperty("reasoningTokens").GetInt64());
        Assert.Equal(0, written.GetProperty("tokenTrackedSessions").GetInt32());
        Assert.Equal(1, written.GetProperty("reasoningTrackedSessions").GetInt32());
        Assert.Equal(7, written.GetProperty("cacheWindowDays").GetInt32());
        var cache = written.GetProperty("cacheUsage");
        Assert.Equal(0, cache.GetProperty("readTokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, cache.GetProperty("writeTokens").ValueKind);
    }

    [Fact]
    public void Stats_OmitsOptionalCoverageWhenTheReportHasNone()
    {
        var report = new SessionStatsReport(0, 0, 0, 0, null, null, null, null, null, null, 0, null, null, []);

        var written = Write(report);

        Assert.False(written.TryGetProperty("tokenTrackedSessions", out _));
        Assert.False(written.TryGetProperty("reasoningTrackedSessions", out _));
        Assert.False(written.TryGetProperty("cacheWindowDays", out _));
        Assert.False(written.TryGetProperty("cacheUsage", out _));
    }

    private static JsonElement Write(SessionStatsReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WireWriter.Stats(writer, "report", report);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.GetProperty("report").Clone();
    }
}
