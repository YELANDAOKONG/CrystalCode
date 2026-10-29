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
    public async Task Options_OfferSideModelTuningOnlyWhileThatModelIsOn()
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
        Assert.Contains(compactionModel.Values, static value => value.Command == "/compact model scripted plain");

        var tier = options.Single(static option => option.Id == "compaction_tier");
        Assert.Equal("_crystal_compaction", tier.Category);
        Assert.Equal("unset", tier.Current);
        Assert.Equal(["unset", "flex", "priority"], tier.Values.Select(static value => value.Value));
        Assert.Equal("/compact tier flex", tier.Values[1].Command);

        var thinking = options.Single(static option => option.Id == "compaction_thinking");
        Assert.Contains(thinking.Values, static value => value.Command == "/compact thinking high");

        var approvalSpeed = options.Single(static option => option.Id == "approval_speed");
        Assert.Equal("_crystal_approval", approvalSpeed.Category);
        Assert.Equal("/approval speed fast", approvalSpeed.Values.Single(static value => value.Value == "fast").Command);

        // plain has no thinking, tiers, or speeds, so extended approval offers none.
        Assert.Contains("extended_approval", options.Select(static option => option.Id));
        Assert.DoesNotContain(options, static option => option.Id.StartsWith("extended_", StringComparison.Ordinal)
            && option.Id != "extended_approval");

        await headless.Session.SubmitAsync(tier.Values[1].Command, CancellationToken.None);
        Assert.Equal(
            "flex",
            headless.Session.ListOptions().Single(static option => option.Id == "compaction_tier").Current);

        await headless.Session.SubmitAsync("/compact model off", CancellationToken.None);
        Assert.DoesNotContain("compaction_tier", Ids(headless));
    }

    [Fact]
    public async Task Chrome_CarriesTheCompactionAndImageModels()
    {
        var settings = Settings().WithImageModel(
            new ImageModelSettings("scripted", "rich", ThinkingSelection.Parse("high"), "priority", "fast"));
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        var started = headless.Observer.Events.OfType<SessionStarted>().Single().Chrome;
        Assert.Null(started.Compaction);
        Assert.Equal(new SideModelChrome("scripted", "rich", "Think High", "Tier Priority", "Speed Fast"), started.Image);

        await headless.Session.SubmitAsync("/compact model scripted rich", CancellationToken.None);
        await headless.Session.SubmitAsync("/compact speed fast", CancellationToken.None);

        Assert.Equal(new SideModelChrome("scripted", "rich", string.Empty, string.Empty, "Speed Fast"), LastChrome(headless).Compaction);

        await headless.Session.SubmitAsync("/compact model off", CancellationToken.None);
        Assert.Null(LastChrome(headless).Compaction);
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
