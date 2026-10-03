using System.Text.Json;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

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

    [Fact]
    public void Render_InlinesImagesRestoredFromBinarySessionStorage()
    {
        using var root = new TemporaryHome();
        var store = new SessionStore(root.Home);
        byte[] png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        var session = new SessionDocument
        {
            Id = "image-export",
            Workspace = "/tmp/demo",
            Items =
            [
                new SessionItemDocument
                {
                    Kind = "message",
                    Role = "user",
                    Text = "See [Image #1]"
                }
            ],
            Images =
            [
                new SessionImageDocument
                {
                    Number = 1,
                    MimeType = "image/png",
                    Data = png
                }
            ]
        };
        store.Save(session);
        Assert.True(store.TryLoad("image-export", out var stored));
        Assert.Null(stored.Images[0].Data);
        session.Images = SessionMapper.WriteImages(store.ReadImages(stored.Images).Values);
        var metadata = new SessionExportMetadata(
            "image-export", "/tmp/demo", "deepseek", "deepseek-flash", "default",
            false, DateTimeOffset.UtcNow);

        var json = SessionJsonExport.Render(metadata, session, null);

        using var export = JsonDocument.Parse(json);
        var image = export.RootElement.GetProperty("session").GetProperty("images")[0];
        Assert.Equal(Convert.ToBase64String(png), image.GetProperty("data").GetString());
        Assert.False(image.TryGetProperty("contentHash", out _));
    }
}
