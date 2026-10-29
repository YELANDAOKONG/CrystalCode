using System.Text.Json;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionCompactionSlotTests
{
    [Fact]
    public async Task CompactTierAndSpeed_CycleSetPersistAndReachTheClient()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/compact tier", CancellationToken.None);
        Assert.Equal("Set a compaction model before changing its service tier.", LastError(headless));

        await headless.Session.SubmitAsync("/compact model brief", CancellationToken.None);
        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact tier", CancellationToken.None);
        Assert.Equal("Compaction tier  Flex", LastNote(headless));
        Assert.Equal(("brief", "flex", (string?)null), headless.ChatPlugin.Slots[^1]);

        await headless.Session.SubmitAsync("/compact speed fast", CancellationToken.None);
        Assert.Equal("Compaction speed  Fast", LastNote(headless));
        Assert.Equal(("brief", "flex", "fast"), headless.ChatPlugin.Slots[^1]);

        var saved = Read(headless).CompactionModel!;
        Assert.Equal("flex", saved.ServiceTier);
        Assert.Equal("fast", saved.Speed);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        var status = headless.Observer.Events.OfType<StatusReported>().Single().Status;
        Assert.Contains("Tier Flex", status.CompactionModel, StringComparison.Ordinal);
        Assert.Contains("Speed Fast", status.CompactionModel, StringComparison.Ordinal);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact speed warp", CancellationToken.None);
        Assert.Equal(
            "Speed is not available for the compaction model. Available: fast",
            LastError(headless));

        await headless.Session.SubmitAsync("/compact model plain", CancellationToken.None);
        var cleared = Read(headless).CompactionModel!;
        Assert.Null(cleared.ServiceTier);
        Assert.Null(cleared.Speed);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact tier", CancellationToken.None);
        Assert.Equal("Service tier is not available for the compaction model.", LastError(headless));
    }

    [Fact]
    public async Task CompactMenu_CompletesTheCompactionModelSlots()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/compact model brief", CancellationToken.None);

        var compact = headless.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands
            .Single(command => command.Name == "compact");
        var tier = compact.Arguments!.Single(argument => argument.Name == "tier");
        var speed = compact.Arguments!.Single(argument => argument.Name == "speed");

        Assert.Equal(["flex", "priority"], tier.Arguments!.Select(item => item.Name));
        Assert.Equal(["fast"], speed.Arguments!.Select(item => item.Name));
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
                ["brief"] = new(200_000, serviceTiers: ["flex", "priority"], speeds: ["fast"]),
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
