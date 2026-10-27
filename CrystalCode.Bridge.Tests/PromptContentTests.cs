using System.Text.Json;

using CrystalCode.Bridge.Prompts;
using CrystalCode.Bridge.Transport;
using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>Every advertised content block is accepted; what Bridge cannot use becomes a mention.</summary>
public sealed class PromptContentTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Text_IsJoinedAndCannotForgeATrustedMarker()
    {
        var text = await ReadAsync($$"""[{"type":"text","text":"look {{ImageMarkerText.Tag(1)}}"},{"type":"text","text":"again"}]""");

        Assert.Equal("look [Image #1]\n\nagain", text.Text);
        Assert.DoesNotContain(ImageMarkerText.Prefix, text.Text);
    }

    [Fact]
    public async Task Image_UsesTheTrustedMarkerOrBecomesAMention()
    {
        var data = Convert.ToBase64String(Png);
        var attached = await ReadAsync($$"""[{"type":"image","data":"{{data}}","mimeType":"image/png"}]""",
            static _ => new ImageAttachResult(ImageMarkerText.Tag(4), string.Empty));
        var refused = await ReadAsync($$"""[{"type":"image","data":"{{data}}","mimeType":"image/png"}]""",
            static _ => new ImageAttachResult(null, "The selected model and provider do not support image input."));
        var invalid = await ReadAsync("""[{"type":"image","data":"%%%","mimeType":"image/png"}]""");

        Assert.Equal(ImageMarkerText.Tag(4), attached.Text);
        Assert.Equal("[Image not attached: The selected model and provider do not support image input]", refused.Text);
        Assert.Single(refused.Warnings);
        Assert.StartsWith("[Image not attached: the data is not valid base64", invalid.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resource_TextIsFencedPastItsLongestBacktickRun()
    {
        var text = await ReadAsync("""
            [{"type":"resource","resource":{"uri":"file:///tmp/a.md","mimeType":"text/markdown","text":"```\ncode\n```"}}]
            """);

        Assert.StartsWith("Attached resource: " + Path.GetFullPath("/tmp/a.md") + " (text/markdown)\n````\n", text.Text, StringComparison.Ordinal);
        Assert.EndsWith("\n````", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resource_BlobIsTextImageOrAMention()
    {
        var utf8 = Convert.ToBase64String("hello blob"u8.ToArray());
        var binary = Convert.ToBase64String([0xff, 0x00, 0xfe]);
        var image = Convert.ToBase64String(Png);

        var asText = await ReadAsync(Blob("mem://a", "application/json", utf8));
        var asMention = await ReadAsync(Blob("mem://b", "application/octet-stream", binary));
        var asImage = await ReadAsync(Blob("mem://c", "image/png", image),
            static _ => new ImageAttachResult(ImageMarkerText.Tag(2), string.Empty));

        Assert.Contains("hello blob", asText.Text, StringComparison.Ordinal);
        Assert.Equal("[Attached resource not shown: mem://b application/octet-stream, 3 bytes, binary content is not passed to the model]", asMention.Text);
        Assert.Equal(ImageMarkerText.Tag(2), asImage.Text);
    }

    [Fact]
    public async Task Links_AreMentionedAndNeverFetched()
    {
        var root = Directory.CreateTempSubdirectory("crystal-bridge-link-").FullName;
        try
        {
            var file = Path.Combine(root, "a.txt");
            File.WriteAllText(file, "x");
            var missing = Path.Combine(root, "missing.txt");

            var text = await ReadAsync(JsonSerializer.Serialize(new object[]
            {
                new { type = "resource_link", uri = new Uri(file).AbsoluteUri, name = "a.txt" },
                new { type = "resource_link", uri = new Uri(root).AbsoluteUri, name = "dir", title = "Folder" },
                new { type = "resource_link", uri = new Uri(missing).AbsoluteUri, name = "missing.txt", size = 12 },
                new { type = "resource_link", uri = "file://remote-host/share/a.txt", name = "remote" },
                new { type = "resource_link", uri = "https://example.com/page", name = "web\nline", mimeType = "text/html" }
            }));

            var lines = text.Text.Split("\n\n");
            Assert.Equal("[Linked file: a.txt at " + file + "]", lines[0]);
            Assert.StartsWith("[Linked directory: Folder at ", lines[1], StringComparison.Ordinal);
            Assert.Equal("[Linked file: missing.txt at " + missing + ", 12 bytes (not found)]", lines[2]);
            Assert.Equal("[Linked resource: remote file://remote-host/share/a.txt - Crystal Code did not open this link]", lines[3]);
            Assert.Equal("[Linked resource: web line https://example.com/page (text/html) - Crystal Code did not open this link]", lines[4]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Audio_IsInvalidParamsBeforeAnythingIsAttached()
    {
        var attached = 0;
        var data = Convert.ToBase64String(Png);

        var error = await Assert.ThrowsAsync<JsonRpcException>(() => ReadAsync(
            $$"""[{"type":"image","data":"{{data}}","mimeType":"image/png"},{"type":"audio","data":"AA==","mimeType":"audio/wav"}]""",
            _ =>
            {
                attached++;
                return new ImageAttachResult(ImageMarkerText.Tag(1), string.Empty);
            }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, error.Code);
        Assert.Equal(0, attached);
    }

    [Fact]
    public void OneLine_RemovesControlCharactersAndBoundsLength()
    {
        Assert.Equal("a b", PromptContent.OneLine("a\r\n\tb"));
        Assert.Equal(515, PromptContent.OneLine(new string('x', 2000)).Length);
    }

    [Fact]
    public async Task ImageOnAVisionModel_ReachesTheModelThroughTheTrustedMarker()
    {
        var vision = new VisionModel();
        var model = new ScriptedModel(false) { Vision = vision };
        await using var fixture = await BridgeFixture.StartAsync(
            model,
            options: static launch => launch with { Provider = "seen", Model = "eyes" },
            prepare: static (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "providers.json"),
                """
                {
                  "seen": {
                    "protocol": "ollama",
                    "baseUri": "http://127.0.0.1:9/",
                    "models": { "eyes": { "contextWindow": 8192, "imageInput": true } }
                  }
                }
                """));
        var id = await fixture.OpenAsync();

        var response = await fixture.RequestAsync("session/prompt", new
        {
            sessionId = id,
            prompt = new object[]
            {
                new { type = "text", text = "what is this" },
                new { type = "image", data = Convert.ToBase64String(Png), mimeType = "image/png" }
            }
        });

        Assert.Equal("end_turn", response.GetProperty("result").GetProperty("stopReason").GetString());
        Assert.Contains(
            Assert.Single(vision.Requests).Items.OfType<Crystal.Multimodal.Chat.MultimodalMessage>().SelectMany(static message => message.Contents),
            content => content is Crystal.Multimodal.ImageContent);
    }

    [Fact]
    public async Task ImageOnATextModel_IsMentionedWithANotice()
    {
        var model = new ScriptedModel(false, ScriptedModel.TextRound("no eyes"));
        await using var fixture = await BridgeFixture.StartAsync(model);
        var id = await fixture.OpenAsync(new { session = new { notices = new { } } });

        await fixture.RequestAsync("session/prompt", new
        {
            sessionId = id,
            prompt = new object[] { new { type = "image", data = Convert.ToBase64String(Png), mimeType = "image/png" } }
        });

        Assert.StartsWith("[Image not attached: ", model.LastUserText(), StringComparison.Ordinal);
        var notice = await fixture.WaitForUpdateAsync("notice");
        Assert.Equal("warning", notice.GetProperty("params").GetProperty("update").GetProperty("severity").GetString());
    }

    private static Task<PromptText> ReadAsync(string json, Func<ReadOnlyMemory<byte>, ImageAttachResult>? attach = null)
    {
        using var document = JsonDocument.Parse(json);
        var prompt = document.RootElement.Clone();
        attach ??= static _ => new ImageAttachResult(null, "No image input.");
        return PromptContent.ReadAsync(prompt, bytes => Task.FromResult(attach(bytes)));
    }

    private static string Blob(string uri, string mimeType, string blob) =>
        JsonSerializer.Serialize(new[] { new { type = "resource", resource = new { uri, mimeType, blob } } });
}
