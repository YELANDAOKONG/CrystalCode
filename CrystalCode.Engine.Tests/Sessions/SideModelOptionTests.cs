using System.Text.Json;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// The option catalog and chrome carry the approval, extended approval,
/// compaction, and image models with their own thinking, tier, and speed.
/// </summary>
public sealed class SideModelOptionTests
{
    [Fact]
    public async Task Options_OfferTuningForTheConfiguredSideModel()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        var initial = Ids(headless);
        Assert.Contains("compaction_model", initial);
        Assert.DoesNotContain("compaction_tier", initial);
        Assert.DoesNotContain("approval_tier", initial);

        await headless.Session.SubmitAsync("/compact model scripted rich", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval model scripted rich", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval extended scripted plain", CancellationToken.None);

        var options = headless.Session.ListOptions();
        var compactionModel = options.Single(static option => option.Id == "compaction_model");
        Assert.Equal("scripted/rich", compactionModel.Current);
        Assert.Contains(compactionModel.Values, static value => value.Command == "/compact model --select scripted plain");

        var tier = options.Single(static option => option.Id == "compaction_tier");
        Assert.Equal("_crystal_compaction", tier.Category);
        Assert.Equal("unset", tier.Current);
        Assert.Equal(["unset", "flex", "priority"], tier.Values.Select(static value => value.Value));
        Assert.Equal("/compact tier flex", tier.Values[1].Command);

        var thinking = options.Single(static option => option.Id == "compaction_thinking");
        Assert.Contains(thinking.Values, static value => value.Command == "/compact thinking high");

        var compactionContext = options.Single(static option => option.Id == "compaction_context");
        Assert.Equal("100000", compactionContext.Current);
        Assert.Equal("/compact context 200000", compactionContext.Values[1].Command);
        Assert.Contains("approval_context", options.Select(static option => option.Id));

        var approvalSpeed = options.Single(static option => option.Id == "approval_speed");
        Assert.Equal("_crystal_approval", approvalSpeed.Category);
        Assert.Equal("/approval speed fast", approvalSpeed.Values.Single(static value => value.Value == "fast").Command);

        // plain has no thinking, tiers, or speeds, so extended approval offers none.
        Assert.Contains("extended_approval", options.Select(static option => option.Id));
        Assert.DoesNotContain(options, static option => option.Id.StartsWith("extended_", StringComparison.Ordinal)
            && option.Id is not ("extended_approval" or "extended_model"));

        await headless.Session.SubmitAsync(tier.Values[1].Command, CancellationToken.None);
        Assert.Equal(
            "flex",
            headless.Session.ListOptions().Single(static option => option.Id == "compaction_tier").Current);

        await headless.Session.SubmitAsync("/compact model off", CancellationToken.None);
        Assert.Equal("flex", headless.Session.ListOptions().Single(static option => option.Id == "compaction_tier").Current);
    }

    [Theory]
    [InlineData("approval_model_enabled", "approval_model", "approvalModel")]
    [InlineData("extended_approval", "extended_model", "extendedApproval")]
    [InlineData("compaction_model_enabled", "compaction_model", "compactionModel")]
    [InlineData("image_model_enabled", "image_model", "imageModel")]
    [InlineData("audio_model_enabled", "audio_model", "audioModel")]
    [InlineData("video_model_enabled", "video_model", "videoModel")]
    [InlineData("document_model_enabled", "document_model", "documentModel")]
    public async Task ModelSelectionAndSwitch_AreIndependent(string switchId, string modelId, string configKey)
    {
        var settings = Settings();
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        Assert.True(Option(switchId).IsSwitch);
        Assert.Equal("off", Option(switchId).Current);
        Assert.Equal("unset", Option(modelId).Current);
        Assert.DoesNotContain(Option(modelId).Values, value => value.Value is "on" or "off");

        await SetAsync(modelId, "scripted/rich");
        Assert.Equal("scripted/rich", Option(modelId).Current);
        Assert.Equal("off", Option(switchId).Current);

        using var config = JsonDocument.Parse(File.ReadAllText(headless.Home.ConfigPath));
        var saved = config.RootElement.GetProperty(configKey);
        Assert.Equal("scripted", saved.GetProperty("provider").GetString());
        Assert.Equal("rich", saved.GetProperty("model").GetString());
        Assert.True(!saved.TryGetProperty("enabled", out var enabled) || enabled.ValueKind == JsonValueKind.False);

        await SetAsync(switchId, "on");
        Assert.Equal("on", Option(switchId).Current);
        await SetAsync(switchId, "off");
        Assert.Equal("off", Option(switchId).Current);
        Assert.Equal("scripted/rich", Option(modelId).Current);
        await SetAsync(switchId, "on");
        Assert.Equal("on", Option(switchId).Current);
        Assert.Empty(headless.Observer.Events.OfType<ErrorWritten>());

        SessionOption Option(string id) => headless.Session.ListOptions().Single(option => option.Id == id);
        Task SetAsync(string id, string value) => headless.RunTurnAsync(Option(id).Values.Single(entry => entry.Value == value).Command);
    }

    [Fact]
    public async Task Chrome_CarriesTheCompactionAndImageModels()
    {
        var settings = Settings().WithResourceModel(
            ResourceKind.Image,
            new ResourceModelSettings(true, "scripted", "rich", ThinkingSelection.Parse("high"), "priority", "fast"));
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        var started = headless.Observer.Events.OfType<SessionStarted>().Single().Chrome;
        Assert.Null(started.Compaction);
        Assert.Equal(new SideModelChrome("scripted", "rich", "Think High", "Tier Priority", "Speed Fast"), started.Image);

        await headless.Session.SubmitAsync("/compact model scripted rich", CancellationToken.None);
        await headless.Session.SubmitAsync("/compact speed fast", CancellationToken.None);

        await headless.Session.SubmitAsync("/compact context 200k", CancellationToken.None);

        Assert.Equal(
            new SideModelChrome("scripted", "rich", string.Empty, string.Empty, "Speed Fast", "Context 200K"),
            LastChrome(headless).Compaction);

        await headless.Session.SubmitAsync("/compact model off", CancellationToken.None);
        Assert.Null(LastChrome(headless).Compaction);
    }

    [Fact]
    public async Task Pursue_OffersCheckerAndCompactionChoicesAndPublishesChanges()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);
        var picker = headless.Session.ListOptions().Single(option => option.Id == "pursue_model");
        Assert.Equal("session", picker.Current);
        Assert.Equal("/pursue model", picker.Values[0].Command);
        Assert.DoesNotContain("pursue_tier", Ids(headless));

        await headless.RunTurnAsync("/pursue model scripted rich");
        await headless.RunTurnAsync("/pursue compaction model scripted rich");
        await headless.RunTurnAsync("/pursue tier priority");
        var published = headless.Observer.Events.OfType<SessionOptionsChanged>().Last().Options;
        Assert.Equal("priority", published.Single(option => option.Id == "pursue_tier").Current);
        Assert.Contains(published.Single(option => option.Id == "pursue_compaction_context").Values,
            value => value.Command == "/pursue compaction context 200000");
        Assert.Contains(published.Single(option => option.Id == "pursue_thinking").Values,
            value => value.Command == "/pursue thinking high");

        await headless.RunTurnAsync("/pursue model");
        await headless.RunTurnAsync("/pursue compaction model");
        Assert.DoesNotContain("pursue_tier", Ids(headless));
        Assert.DoesNotContain("pursue_compaction_context", Ids(headless));
        Assert.Equal("session", headless.Session.ListOptions().Single(option => option.Id == "pursue_compaction_model").Current);
    }

    private static SessionChrome LastChrome(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome;

    private static string[] Ids(HeadlessSession headless) =>
        [.. headless.Session.ListOptions().Select(static option => option.Id)];

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
                ["rich"] = new(
                    200_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high"],
                    imageInput: true,
                    audioInput: true,
                    videoInput: true,
                    fileInput: true,
                    contextWindows: [100_000, 200_000],
                    serviceTiers: ["flex", "priority"],
                    speeds: ["fast"]),
                ["plain"] = new(200_000)
            },
            requiresApiKey: false);
        return new HarnessSettings(
            provider,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([definition]));
    }
}
