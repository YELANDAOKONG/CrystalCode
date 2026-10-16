using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionContextTests
{
    [Fact]
    public async Task Context_CyclesAndSetsBudgetAndRefreshesUsageImmediately()
    {
        var client = new ScriptedStreamingClient([
            new ChatTextDelta(0, 0, ChatRole.Assistant, "Hello"),
            new ChatCandidateCompleted(0, FinishReason.Stop),
            new ChatUsageReceived(new TokenUsage(100, 20))
        ]);
        using var headless = new HeadlessSession(client, settings: Settings(), prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath,
                System.Text.Json.JsonSerializer.Serialize(SettingsMapper.WriteProviders(Settings().Catalog)));
        });
        await headless.Session.StartAsync(CancellationToken.None);
        Assert.Equal(272000, headless.Observer.Events.OfType<SessionStarted>().Single().Chrome.ContextWindow);
        var menu = headless.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands;
        Assert.Equal(["272k", "1m"], menu.Single(item => item.Name == "context").Arguments!.Select(item => item.Name));

        await headless.Session.SubmitAsync("/context", CancellationToken.None);
        Assert.Equal(1000000, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.ContextWindow);
        await headless.RunTurnAsync("hi");
        Assert.Equal(1000000, headless.Observer.Events.OfType<TurnFinished>().Single().ContextWindow);

        await headless.Session.SubmitAsync("/context 272k", CancellationToken.None);
        var usage = headless.Observer.Events.OfType<UsageChanged>().Last();
        Assert.Equal(272000, usage.ContextWindow);
        Assert.Equal(100, usage.Usage?.InputTokenCount);
        Assert.Equal(272000, new SettingsStore(headless.Home).Load().ContextWindow);

        await headless.Session.SubmitAsync("/context 128k", CancellationToken.None);
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("not available", StringComparison.Ordinal));
        Assert.Equal(272000, new SettingsStore(headless.Home).Load().ContextWindow);

        await headless.Session.SubmitAsync("/model small", CancellationToken.None);
        Assert.Equal(128000, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.ContextWindow);
        var smallMenu = headless.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands;
        Assert.Equal("128k", Assert.Single(smallMenu.Single(item => item.Name == "context").ArgumentOptions).Name);
        await headless.Session.SubmitAsync("/model model", CancellationToken.None);
        Assert.Equal(272000, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.ContextWindow);

        await headless.Session.SubmitAsync("/statusline thinking context-window context-used", CancellationToken.None);
        var preferences = headless.Observer.Events.OfType<PreferencesChanged>().Last().Preferences;
        Assert.Contains("context-window", preferences.StatusLineFields);
    }

    [Fact]
    public async Task Context_RefusesChangesDuringAnActiveTurn()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client, settings: Settings());
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("hi", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await headless.Session.SubmitAsync("/context 1m", CancellationToken.None);
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("Finish the current turn", StringComparison.Ordinal));
        headless.Session.TryInterrupt();
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.Equal(272000, headless.Observer.Events.OfType<TurnFinished>().Single().ContextWindow);
    }

    [Fact]
    public async Task Context_LoweringBudgetCompactsBeforeTheNextRequestAndKeepsArchive()
    {
        var head = "EARLIER-CONTENT" + new string('a', 900000);
        var resume = new SessionDocument
        {
            Items =
            [
                new SessionItemDocument { Kind = "message", Role = "user", Text = head },
                new SessionItemDocument { Kind = "message", Role = "assistant", Text = "old answer" },
                new SessionItemDocument { Kind = "message", Role = "user", Text = "recent turn" }
            ]
        };
        var client = new CompactingClient();
        var settings = Settings();
        using var headless = new HeadlessSession(client, resume,
            settings: settings.WithContextWindow(settings.Provider, settings.Model, 1000000));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/context 272k", CancellationToken.None);
        Assert.Equal(0, client.Summaries);
        await headless.RunTurnAsync("continue");

        Assert.Equal(1, client.Summaries);
        Assert.DoesNotContain(client.StreamRequest!.Items.OfType<ChatMessage>(),
            item => item.Text.Contains("EARLIER-CONTENT", StringComparison.Ordinal));
        Assert.Contains(headless.ReadSaved().Archive!, item => item.Text == head);
        Assert.Equal(TurnStopReason.Completed, headless.Observer.Events.OfType<TurnFinished>().Single().Result.StopReason);
    }

    [Fact]
    public async Task AutomaticCompaction_HidesTheSummaryByDefault()
    {
        var notes = await CompactAutomatically(showSummary: false);

        Assert.Contains(notes, note => note == "Compacted context");
        Assert.DoesNotContain(
            notes,
            note => note.Contains("Earlier facts retained", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutomaticCompaction_PrintsTheSummaryWhenConfigured()
    {
        var notes = await CompactAutomatically(showSummary: true);
        var compacted = Array.IndexOf(notes, "Compacted context");
        var summary = Array.FindIndex(
            notes,
            note => note.Contains("Earlier facts retained", StringComparison.Ordinal));

        Assert.True(compacted >= 0);
        Assert.True(summary > compacted);
    }

    private static async Task<string[]> CompactAutomatically(bool showSummary)
    {
        var head = "EARLIER-CONTENT" + new string('x', 60_000);
        var resume = new SessionDocument
        {
            Items =
            [
                new SessionItemDocument { Kind = "message", Role = "user", Text = head },
                new SessionItemDocument { Kind = "message", Role = "assistant", Text = "old answer" },
                new SessionItemDocument { Kind = "message", Role = "user", Text = "recent turn" }
            ]
        };
        var settings = NarrowSettings();
        if (showSummary)
        {
            settings = settings.WithShowAutomaticCompactionSummary(true);
        }

        using var headless = new HeadlessSession(new CompactingClient(), resume, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("continue");
        Assert.Equal(
            TurnStopReason.Completed,
            headless.Observer.Events.OfType<TurnFinished>().Single().Result.StopReason);
        return [.. headless.Observer.Events.OfType<NoteWritten>().Select(note => note.Text)];
    }

    private static HarnessSettings NarrowSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(32_000) },
            requiresApiKey: false);
        return new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]));
    }

    private sealed class CompactingClient : IStreamingChatClient
    {
        public int Summaries { get; private set; }

        public ChatRequest? StreamRequest { get; private set; }

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            StreamRequest = request;
            return new ScriptedStreamingClient([
                new ChatTextDelta(0, 0, ChatRole.Assistant, "done"),
                new ChatCandidateCompleted(0, FinishReason.Stop)
            ]).StreamAsync(request, cancellationToken);
        }

        public Task<ChatResponse> CompleteAsync(ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Summaries++;
            return Task.FromResult(new ChatResponse([
                new ChatCandidate([new ChatMessage(ChatRole.Assistant, "Earlier facts retained.")], FinishReason.Stop)
            ]));
        }
    }

    private static HarnessSettings Settings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>
            {
                ["model"] = new(1000000, contextWindows: [272000, 1000000]),
                ["small"] = new(128000)
            },
            requiresApiKey: false);
        return new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]));
    }
}
