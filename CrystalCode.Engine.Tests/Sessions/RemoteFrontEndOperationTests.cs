using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Operations a remote front end needs in place of the local clipboard, the
/// local export directory, and printed exit copy.
/// </summary>
public sealed class RemoteFrontEndOperationTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00];

    [Fact]
    public async Task TryAttachImage_AddsADraftImageTheSessionCanReturn()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: VisionSettings(),
            images: new ChildVisionClient());
        await headless.Session.StartAsync(CancellationToken.None);

        Assert.True(headless.Session.TryAttachImage(Png, out var image, out var error), error);

        Assert.NotNull(image);
        Assert.Equal("image/png", image.MimeType);
        Assert.StartsWith(ImageMarkerText.Prefix.ToString(), image.TrustedMarker, StringComparison.Ordinal);
        Assert.True(headless.Session.TryGetImage(image.Number, out var held));
        Assert.Equal(Png, held!.Data!.Value.ToArray());

        headless.Session.NotifyDraftChanged(string.Empty);
        Assert.False(headless.Session.TryGetImage(image.Number, out _));
    }

    [Fact]
    public async Task TryAttachImage_RejectsUnsupportedBytesAndTextModels()
    {
        using var vision = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: VisionSettings(),
            images: new ChildVisionClient());
        await vision.Session.StartAsync(CancellationToken.None);
        Assert.False(vision.Session.TryAttachImage("not an image"u8.ToArray(), out _, out var bytesError));
        Assert.Contains("PNG, JPEG, GIF, or WebP", bytesError, StringComparison.Ordinal);
        Assert.False(vision.Session.TryAttachImage(ReadOnlyMemory<byte>.Empty, out _, out var emptyError));
        Assert.Equal("The image is empty.", emptyError);

        using var text = new HeadlessSession(new ScriptedStreamingClient());
        await text.Session.StartAsync(CancellationToken.None);
        Assert.False(text.Session.TryAttachImage(Png, out _, out var modelError));
        Assert.Contains("do not support image input", modelError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryExport_RendersWithoutWritingAFile()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(TextRound("Hello there.")));
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("hi");
        var exports = Path.Combine(headless.Home.Root, "exports");

        Assert.True(headless.Session.TryExport("markdown", false, out var markdown, out var markdownError), markdownError);
        Assert.True(headless.Session.TryExport("JSON", true, out var json, out var jsonError), jsonError);

        Assert.Equal(headless.Session.SessionId + ".md", markdown!.FileName);
        Assert.Contains("Hello there.", markdown.Text, StringComparison.Ordinal);
        Assert.Equal("application/json", json!.MediaType);
        using var document = JsonDocument.Parse(json.Text);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.False(Directory.Exists(exports));
        Assert.False(headless.Session.TryExport("yaml", false, out _, out var formatError));
        Assert.Equal("Export format must be markdown or json.", formatError);
    }

    [Fact]
    public async Task CloseAsync_ReportsWhetherTheSessionWasSaved()
    {
        using var empty = new HeadlessSession(new ScriptedStreamingClient());
        await empty.Session.StartAsync(CancellationToken.None);
        var unsaved = await empty.Session.CloseAsync();
        Assert.False(unsaved.Saved);
        Assert.Equal(empty.Session.SessionId, unsaved.SessionId);

        using var used = new HeadlessSession(new ScriptedStreamingClient(TextRound("done")));
        await used.Session.StartAsync(CancellationToken.None);
        await used.RunTurnAsync("hi");
        var saved = await used.Session.CloseAsync();
        Assert.True(saved.Saved);
        Assert.Contains(used.Session.SessionId, saved.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChildInspection_WithoutAPickerUsesNeutralText()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/child", CancellationToken.None);

        var error = headless.Observer.Events.OfType<ErrorWritten>().Last().Text;
        Assert.Equal("Child inspection is not available in this front end.", error);
    }

    private static HarnessSettings VisionSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(200_000, imageInput: true) });
        return new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            new ProviderCatalog([provider]));
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
