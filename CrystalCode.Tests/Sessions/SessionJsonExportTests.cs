using CrystalCode.Home;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class SessionJsonExportTests
{
    [Fact]
    public void Render_WritesExportEnvelopeWithoutSystemByDefault()
    {
        var metadata = new SessionExportMetadata(
            "abc123",
            "/tmp/demo",
            "deepseek",
            "deepseek-v4-flash",
            "default",
            true,
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var session = new SessionDocument
        {
            Id = "abc123",
            Workspace = "/tmp/demo",
            Items = []
        };

        var json = SessionJsonExport.Render(metadata, session, null);

        Assert.Contains("\"format\": \"crystalcode.session.export\"", json, StringComparison.Ordinal);
        Assert.Contains("\"modelLine\": \"deepseek / deepseek-v4-flash\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"system\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesSystemWhenRequested()
    {
        var metadata = new SessionExportMetadata(
            "abc123",
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "default",
            false,
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var session = new SessionDocument { Id = "abc123" };

        var json = SessionJsonExport.Render(metadata, session, "system body");

        Assert.Contains("\"system\": \"system body\"", json, StringComparison.Ordinal);
    }
}
