using System.Text.Json;

using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionSlotTests
{
    [Fact]
    public async Task TierAndSpeed_CycleSetAndPersistPerModel()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);
        var menu = headless.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands;
        Assert.Equal(["flex", "default"], menu.Single(item => item.Name == "tier").Arguments!.Select(item => item.Name));
        Assert.Equal(
            ["fast", "ultrafast"],
            menu.Single(item => item.Name == "speed").Arguments!.Select(item => item.Name));

        await headless.Session.SubmitAsync("/tier", CancellationToken.None);
        Assert.Equal("Tier Flex", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.ServiceTier);
        await headless.Session.SubmitAsync("/tier default", CancellationToken.None);
        Assert.Equal("Tier Default", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.ServiceTier);
        await headless.Session.SubmitAsync("/fast", CancellationToken.None);
        var chrome = headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome;
        Assert.Equal("Tier Default", chrome.ServiceTier);
        Assert.Equal("Speed Fast", chrome.Speed);
        await headless.Session.SubmitAsync("/speed", CancellationToken.None);
        Assert.Equal("Speed Ultrafast", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Speed);
        await headless.Session.SubmitAsync("/speed", CancellationToken.None);
        Assert.Equal(string.Empty, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Speed);

        await headless.Session.SubmitAsync("/tier missing", CancellationToken.None);
        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("not available", StringComparison.Ordinal));

        using var saved = JsonDocument.Parse(File.ReadAllText(headless.Home.ConfigPath));
        var tier = saved.RootElement.GetProperty("tierSelections").GetProperty("scripted").GetProperty("model");
        Assert.Equal("default", tier.GetString());
        Assert.False(saved.RootElement.TryGetProperty("speedSelections", out _));
    }

    [Fact]
    public async Task Tier_RefusesChangesDuringAnActiveTurn()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client, settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("hi", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await headless.Session.SubmitAsync("/tier flex", CancellationToken.None);
        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "Finish the current turn before switching service tier.");
        headless.Session.TryInterrupt();
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Tier_RebuildsTheCompactionClientWhenItUsesTheSameModel()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);
        var started = headless.ChatPlugin.Creates;

        await headless.Session.SubmitAsync("/compact model model", CancellationToken.None);
        var withCompaction = headless.ChatPlugin.Creates;
        Assert.True(withCompaction > started);

        await headless.Session.SubmitAsync("/tier flex", CancellationToken.None);

        Assert.Equal(withCompaction + 2, headless.ChatPlugin.Creates);
        Assert.Equal("Tier Flex", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.ServiceTier);
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
                ["model"] = new(
                    200_000,
                    serviceTiers: ["flex", "default"],
                    speeds: ["fast", "ultrafast"])
            });
        return new HarnessSettings(
            provider,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([definition]));
    }
}
