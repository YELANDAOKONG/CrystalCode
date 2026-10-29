using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionResourceModelTests
{
    [Fact]
    public async Task DescribeImage_SetsTheModelAndRegistersTheToolOnlyWhileOn()
    {
        var model = new ScriptedStreamingClient(Text("one"), Text("two"), Text("three"));
        using var headless = new HeadlessSession(model, settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("first");
        Assert.DoesNotContain(model.LastRequest!.Tools, static tool => tool.Name == "describe_image");

        await headless.Session.SubmitAsync("/describe image model scripted eyes", CancellationToken.None);
        Assert.Equal("Image model  On  scripted  eyes", LastNote(headless));
        var saved = Read(headless).ImageModel!;
        Assert.True(saved.Enabled);
        Assert.Equal("eyes", saved.Model);

        await headless.RunTurnAsync("second");
        Assert.Contains(model.LastRequest!.Tools, static tool => tool.Name == "describe_image");

        foreach (var command in new[]
                 {
                     "/describe image thinking high",
                     "/describe image tier priority",
                     "/describe image speed fast",
                     "/describe image context 200k"
                 })
        {
            await headless.Session.SubmitAsync(command, CancellationToken.None);
        }

        Assert.Empty(headless.Observer.Events.OfType<ErrorWritten>());
        saved = Read(headless).ImageModel!;
        Assert.Equal("high", saved.ThinkingEffort);
        Assert.Equal("priority", saved.ServiceTier);
        Assert.Equal("fast", saved.Speed);
        Assert.Equal(200_000, saved.ContextWindow);
        Assert.Equal(
            new SideModelChrome("scripted", "eyes", "Think High", "Tier Priority", "Speed Fast", "Context 200K"),
            headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Image);

        await headless.Session.SubmitAsync("/describe image off", CancellationToken.None);
        var off = Read(headless).ImageModel!;
        Assert.Null(off.Enabled);
        Assert.Equal("eyes", off.Model);
        Assert.Equal("priority", off.ServiceTier);
        Assert.Null(headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Image);

        await headless.RunTurnAsync("third");
        Assert.DoesNotContain(model.LastRequest!.Tools, static tool => tool.Name == "describe_image");
    }

    [Fact]
    public async Task DescribeAudioAndVideo_SaveTheSettingAndSayItIsNotConnected()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/describe audio model scripted ears", CancellationToken.None);
        await headless.Session.SubmitAsync("/describe video model scripted reel", CancellationToken.None);
        await headless.Session.SubmitAsync("/describe video context", CancellationToken.None);

        Assert.Empty(headless.Observer.Events.OfType<ErrorWritten>());
        var document = Read(headless);
        Assert.True(document.AudioModel!.Enabled);
        Assert.Equal("ears", document.AudioModel.Model);
        Assert.Equal("reel", document.VideoModel!.Model);
        Assert.Equal(200_000, document.VideoModel.ContextWindow);
        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            static note => note.Text == "Audio description is not connected yet. The setting is saved for a later release.");

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/describe", CancellationToken.None);
        var notes = headless.Observer.Events.OfType<NoteWritten>().Select(static note => note.Text).ToArray();
        Assert.Equal(
            [
                "Image model  Off",
                "Audio model  On  scripted  ears  ·  Not connected yet",
                "Video model  On  scripted  reel  ·  Context 200K  ·  Not connected yet"
            ],
            notes);
    }

    [Fact]
    public async Task Describe_RejectsAModelWithoutThatInputAndAnUnknownForm()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/describe audio model scripted eyes", CancellationToken.None);
        Assert.Equal(
            "Audio model 'eyes' does not accept audio input. Set audioInput on that model in providers.json.",
            LastError(headless));

        await headless.Session.SubmitAsync("/describe video on", CancellationToken.None);
        Assert.Equal("Set the video model before turning it on.", LastError(headless));

        await headless.Session.SubmitAsync("/describe sound", CancellationToken.None);
        Assert.StartsWith("Use /describe <image|audio|video>", LastError(headless), StringComparison.Ordinal);
        Assert.False(File.Exists(headless.Home.ConfigPath) && Read(headless).AudioModel is not null);
    }

    [Fact]
    public async Task DescribeOptions_OfferOnlyModelsWithThatInput()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        var options = headless.Session.ListOptions();
        var audio = options.Single(static option => option.Id == "audio_model");

        Assert.Equal("off", audio.Current);
        Assert.Equal("_crystal_describe", audio.Category);
        Assert.Equal(["off", "scripted/ears"], audio.Values.Select(static value => value.Value));
        Assert.Equal("/describe audio model scripted ears", audio.Values[1].Command);
        Assert.DoesNotContain(options, static option => option.Id == "audio_context");

        await headless.Session.SubmitAsync("/describe image model scripted eyes", CancellationToken.None);
        Assert.Contains(headless.Session.ListOptions(), static option => option.Id == "image_tier");
    }

    private static HarnessSettings Settings()
    {
        var provider = new ProviderName("scripted");
        var definition = new ProviderDefinition(
            provider,
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["model"] = new(200_000),
                ["eyes"] = new(
                    200_000,
                    thinking: true,
                    thinkingEfforts: ["high"],
                    imageInput: true,
                    contextWindows: [100_000, 200_000],
                    serviceTiers: ["priority"],
                    speeds: ["fast"]),
                ["ears"] = new(200_000, audioInput: true),
                ["reel"] = new(200_000, videoInput: true, contextWindows: [100_000, 200_000])
            },
            requiresApiKey: false);
        return new HarnessSettings(
            provider,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([definition]));
    }

    private static IReadOnlyList<ChatStreamEvent> Text(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatUsageReceived(new TokenUsage(10, 2, 1)),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static string LastNote(HeadlessSession headless) =>
        headless.Observer.Events.OfType<NoteWritten>().Last().Text;

    private static string LastError(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ErrorWritten>().Last().Text;

    private static SettingsDocument Read(HeadlessSession headless)
    {
        var json = File.ReadAllText(headless.Home.ConfigPath);
        return JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? throw new InvalidOperationException("Settings file was empty.");
    }
}
