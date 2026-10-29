using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionTopicTests
{
    [Fact]
    public async Task FirstTurn_NamesTheSessionWithTheSessionModelOnceAndSavesIt()
    {
        var model = new ScriptedStreamingClient(Text("Done."), Text("\"Fix the login redirect.\""), Text("Again."));
        var settings = Settings().WithTopicModel(new TopicModelSettings(true));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("The login page redirects twice.");
        await headless.Session.TopicTask;

        Assert.Equal(2, model.RequestCount);
        var request = model.LastRequest!;
        Assert.Empty(request.Tools);
        Assert.Contains("concise topic name", ((ChatMessage)request.Items[0]).Text, StringComparison.Ordinal);
        Assert.Contains("The login page redirects twice.", ((ChatMessage)request.Items[1]).Text, StringComparison.Ordinal);
        var saved = headless.ReadSaved();
        Assert.Equal("Fix the login redirect", saved.Topic);
        Assert.Equal("model", saved.TopicSource);
        Assert.Equal("Fix the login redirect", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Topic);
        var summary = Assert.Single(new SessionStore(headless.Home).List(headless.WorkspacePath));
        Assert.Equal("Fix the login redirect", summary.Title);
        Assert.Equal("The login page redirects twice.", summary.Preview);

        await headless.RunTurnAsync("Also check logout.");
        await headless.Session.TopicTask;
        Assert.Equal(3, model.RequestCount);
    }

    [Fact]
    public async Task Off_DoesNotCallTheModelAndAnOlderSessionListsByItsFirstMessage()
    {
        var model = new ScriptedStreamingClient(Text("Done."));
        using var headless = new HeadlessSession(model, settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("Read the readme.");
        await headless.Session.TopicTask;

        Assert.Equal(1, model.RequestCount);
        Assert.Null(headless.ReadSaved().Topic);
        Assert.Equal("Read the readme.", Assert.Single(new SessionStore(headless.Home).List(headless.WorkspacePath)).Title);
    }

    [Fact]
    public async Task UnclearAnswer_LeavesTheSessionUnnamedAndIsNotRetried()
    {
        var model = new ScriptedStreamingClient(Text("Hi."), Text("New conversation"), Text("Again."));
        using var headless = new HeadlessSession(model, settings: Settings().WithTopicModel(new TopicModelSettings(true)));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("hello");
        await headless.Session.TopicTask;
        await headless.RunTurnAsync("hello again");
        await headless.Session.TopicTask;

        Assert.Equal(3, model.RequestCount);
        Assert.Null(headless.ReadSaved().Topic);
    }

    [Fact]
    public async Task OperatorTopic_IsKeptAndRegenerateReplacesIt()
    {
        var model = new ScriptedStreamingClient(Text("Done."), Text("Refactor the parser"));
        using var headless = new HeadlessSession(model, settings: Settings().WithTopicModel(new TopicModelSettings(true)));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/topic Parser cleanup", CancellationToken.None);
        await headless.RunTurnAsync("Clean up the parser.");
        await headless.Session.TopicTask;

        Assert.Equal(1, model.RequestCount);
        Assert.Equal("Parser cleanup", headless.ReadSaved().Topic);
        Assert.Equal("operator", headless.ReadSaved().TopicSource);

        await headless.Session.SubmitAsync("/topic regenerate", CancellationToken.None);
        await headless.Session.TopicTask;
        Assert.Equal("Refactor the parser", headless.ReadSaved().Topic);
        Assert.Equal("model", headless.ReadSaved().TopicSource);

        await headless.Session.SubmitAsync("/topic clear", CancellationToken.None);
        Assert.Null(headless.ReadSaved().Topic);
    }

    [Fact]
    public async Task OwnModel_IsConfiguredOnItsOwnAndTheSessionModelFollowsTheSession()
    {
        var model = new ScriptedStreamingClient(Text("Done."), Text("Name it"));
        var settings = Settings()
            .WithTier(new ProviderName("scripted"), "namer", "flex")
            .WithTier(new ProviderName("scripted"), "model", "flex")
            .WithTopicModel(new TopicModelSettings(true));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        Assert.Equal("flex", headless.Session.ListOptions().Single(static option => option.Id == "tier").Current);
        Assert.DoesNotContain(headless.Session.ListOptions(), static option => option.Id == "topic_tier");
        await headless.Session.SubmitAsync("/topic tier flex", CancellationToken.None);
        Assert.StartsWith("The topic model uses the session model", LastError(headless), StringComparison.Ordinal);

        foreach (var command in new[] { "/topic model scripted namer", "/topic thinking high", "/topic speed fast", "/topic context 200k" })
        {
            await headless.Session.SubmitAsync(command, CancellationToken.None);
        }

        Assert.Single(headless.Observer.Events.OfType<ErrorWritten>());
        var stored = Read(headless).TopicModel!;
        Assert.True(stored.Enabled);
        Assert.Equal("namer", stored.Model);
        Assert.Equal("high", stored.ThinkingEffort);
        Assert.Equal("fast", stored.Speed);
        Assert.Equal(200_000, stored.ContextWindow);
        Assert.Null(stored.ServiceTier);
        Assert.Contains(headless.Session.ListOptions(), static option => option.Id == "topic_tier");

        await headless.RunTurnAsync("Name the session.");
        await headless.Session.TopicTask;

        // The session tier for the same model is not sent; the topic model's own speed is.
        Assert.Equal(("namer", (string?)null, "fast"), headless.ChatPlugin.Slots[^1]);
        Assert.Equal("Name it", headless.ReadSaved().Topic);

        await headless.Session.SubmitAsync("/topic model", CancellationToken.None);
        var reset = Read(headless).TopicModel!;
        Assert.Null(reset.Model);
        Assert.Null(reset.Speed);
        Assert.Null(reset.ContextWindow);
        Assert.Equal("high", reset.ThinkingEffort);

        await headless.Session.SubmitAsync("/topic off", CancellationToken.None);
        Assert.Null(Read(headless).TopicModel!.Enabled);
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
                ["model"] = new(200_000, serviceTiers: ["flex"]),
                ["namer"] = new(
                    200_000,
                    thinking: true,
                    thinkingEfforts: ["high"],
                    contextWindows: [100_000, 200_000],
                    serviceTiers: ["flex"],
                    speeds: ["fast"])
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

    private static string LastError(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ErrorWritten>().Last().Text;

    private static SettingsDocument Read(HeadlessSession headless)
    {
        var json = File.ReadAllText(headless.Home.ConfigPath);
        return JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? throw new InvalidOperationException("Settings file was empty.");
    }
}
