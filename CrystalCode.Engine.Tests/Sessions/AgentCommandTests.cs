using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Home.Settings;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class AgentCommandTests
{
    [Fact]
    public async Task AgentCommands_SaveOwnModelOptionsAndUseItsCapabilitiesForCompletions()
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            {
                ["model"] = new(200_000),
                ["child"] = new(200_000, thinking: true, thinkingEfforts: ["high"],
                    contextWindows: [100_000, 200_000], serviceTiers: ["priority"], speeds: ["fast"])
            });
        var settings = new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8, new ProviderCatalog([provider]));
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: settings, prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
        });
        await session.Session.StartAsync(CancellationToken.None);
        foreach (var command in new[] { "/agents on", "/agents concurrency 2", "/agents model explore scripted child",
            "/agents thinking explore high", "/agents context explore 200k", "/agents tier explore priority", "/agents speed explore fast" })
        {
            await session.Session.SubmitAsync(command, CancellationToken.None);
        }
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
        var stored = new DiskSettingsStore(session.Home).Load().Agents;
        Assert.True(stored.Enabled);
        Assert.Equal(2, stored.MaxConcurrent);
        Assert.Equal("child", stored.Explore.Model);
        Assert.Equal("high", stored.Explore.Thinking);
        Assert.Equal(200_000, stored.Explore.Context);
        Assert.Equal("priority", stored.Explore.Tier);
        Assert.Equal("fast", stored.Explore.Speed);
        var completion = session.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands.Single(command => command.Name == "agents");
        Assert.Contains(completion.ArgumentOptions.Single(command => command.Name == "thinking").ArgumentOptions
            .Single(agent => agent.Name == "explore").ArgumentOptions, gear => gear.Name == "high");
        await session.Session.SubmitAsync("/agents thinking explore", CancellationToken.None);
        Assert.Null(new DiskSettingsStore(session.Home).Load().Agents.Explore.Thinking);
        await session.Session.SubmitAsync("/agents concurrency 9", CancellationToken.None);
        Assert.Equal(2, new DiskSettingsStore(session.Home).Load().Agents.MaxConcurrent);
        Assert.NotEmpty(session.Observer.Events.OfType<ErrorWritten>());
    }

    [Fact]
    public async Task AgentCompaction_SavesPerAgentAndClearsAndRejectsBeforeModel()
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            {
                ["model"] = new(200_000),
                ["summary"] = new(200_000, thinking: true, thinkingEfforts: ["high"], contextWindows: [100_000, 200_000])
            });
        var settings = new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8, new ProviderCatalog([provider]));
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: settings, prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
        });
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("/agents on", CancellationToken.None);

        await session.Session.SubmitAsync("/agents compaction explore thinking high", CancellationToken.None);
        Assert.Single(session.Observer.Events.OfType<ErrorWritten>());

        foreach (var command in new[] { "/agents compaction explore model scripted summary",
            "/agents compaction explore thinking high", "/agents compaction explore context 100k" })
        {
            await session.Session.SubmitAsync(command, CancellationToken.None);
        }

        var stored = new DiskSettingsStore(session.Home).Load().Agents;
        Assert.Equal("summary", stored.Explore.Compaction!.Model);
        Assert.Equal("high", stored.Explore.Compaction.Thinking);
        Assert.Equal(100_000, stored.Explore.Compaction.Context);
        Assert.Null(stored.Work.Compaction);

        await session.Session.SubmitAsync("/agents compaction explore clear", CancellationToken.None);
        Assert.Null(new DiskSettingsStore(session.Home).Load().Agents.Explore.Compaction);
    }

    [Fact]
    public async Task AgentsOnAndOff_AddAndRemoveTheTaskToolForTheNextRequest()
    {
        var settings = HeadlessSession.ScriptedSettings();
        var model = new ScriptedStreamingClient(Done(), Done(), Done());
        using var session = new HeadlessSession(model, settings: settings, prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
        });
        await session.Session.StartAsync(CancellationToken.None);

        await session.RunTurnAsync("first");
        Assert.DoesNotContain(model.LastRequest!.Tools, tool => tool.Name == "task");

        await session.Session.SubmitAsync("/agents on", CancellationToken.None);
        await session.RunTurnAsync("second");
        Assert.Contains(model.LastRequest!.Tools, tool => tool.Name == "task");

        await session.Session.SubmitAsync("/agents off", CancellationToken.None);
        await session.RunTurnAsync("third");
        Assert.DoesNotContain(model.LastRequest!.Tools, tool => tool.Name == "task");
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
    }

    [Fact]
    public async Task AgentsToggledFromEnabled_KeepsTheTaskToolInWorkAndPlan()
    {
        var settings = HeadlessSession.ScriptedSettings().WithAgents(new AgentSettings { Enabled = true });
        var model = new ScriptedStreamingClient(Done(), Done(), Done(), Done(), Done());
        using var session = new HeadlessSession(model, settings: settings, prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
        });
        await session.Session.StartAsync(CancellationToken.None);

        await session.RunTurnAsync("a");
        Assert.Contains(model.LastRequest!.Tools, tool => tool.Name == "task");

        await session.Session.SubmitAsync("/agents off", CancellationToken.None);
        await session.Session.SubmitAsync("/agents on", CancellationToken.None);
        await session.RunTurnAsync("b");
        Assert.Contains(model.LastRequest!.Tools, tool => tool.Name == "task");

        await session.Session.SubmitAsync("/plan", CancellationToken.None);
        await session.RunTurnAsync("c");
        Assert.Contains(model.LastRequest!.Tools, tool => tool.Name == "task");
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentsOn_AfterResumeAddsTheTaskToolForTheNextRequest(bool startEnabled)
    {
        var settings = HeadlessSession.ScriptedSettings().WithAgents(new AgentSettings { Enabled = startEnabled });
        using var first = new HeadlessSession(new ScriptedStreamingClient(Done()), settings: settings);
        await first.Session.StartAsync(CancellationToken.None);
        await first.RunTurnAsync("first");
        var saved = first.ReadSaved();

        var model = new ScriptedStreamingClient(Done(), Done());
        using var resumed = new HeadlessSession(model, resume: saved, settings: settings, prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
        });
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.Session.SubmitAsync("/agents off", CancellationToken.None);
        await resumed.Session.SubmitAsync("/agents on", CancellationToken.None);
        await resumed.RunTurnAsync("second");

        Assert.Contains(model.LastRequest!.Tools, tool => tool.Name == "task");
        Assert.Empty(resumed.Observer.Events.OfType<ErrorWritten>());
    }

    private static IReadOnlyList<ChatStreamEvent> Done() =>
        [new ChatTextDelta(0, 0, ChatRole.Assistant, "done"), new ChatUsageReceived(new TokenUsage(10, 2, 1)),
            new ChatCandidateCompleted(0, FinishReason.Stop)];

    [Fact]
    public async Task AgentLimitCommand_SavesEachFieldAndClearsItWithoutAValue()
    {
        var settings = HeadlessSession.ScriptedSettings();
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: settings, prepare: (home, _) =>
        {
            home.EnsureCreated();
            File.WriteAllText(home.ProvidersPath, JsonSerializer.Serialize(SettingsMapper.WriteProviders(settings.Catalog)));
        });
        await session.Session.StartAsync(CancellationToken.None);
        foreach (var command in new[] { "/agents limit model-calls 300", "/agents limit tool-calls 2000", "/agents limit duration 7200" })
        {
            await session.Session.SubmitAsync(command, CancellationToken.None);
        }
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
        Assert.Equal(new AgentLimits(300, 2000, 7200), new DiskSettingsStore(session.Home).Load().Agents.Limits);

        await session.Session.SubmitAsync("/agents limit tool-calls", CancellationToken.None);
        Assert.Equal(new AgentLimits(300, null, 7200), new DiskSettingsStore(session.Home).Load().Agents.Limits);

        await session.Session.SubmitAsync("/agents limit model-calls 0", CancellationToken.None);
        await session.Session.SubmitAsync("/agents limit bogus 5", CancellationToken.None);
        Assert.Equal(2, session.Observer.Events.OfType<ErrorWritten>().Count());
        Assert.Equal(new AgentLimits(300, null, 7200), new DiskSettingsStore(session.Home).Load().Agents.Limits);
    }

    [Fact]
    public async Task AgentsList_IsOneDocumentWithSettingsAndATableOfDefinitions()
    {
        var settings = HeadlessSession.ScriptedSettings().WithAgents(new AgentSettings { Enabled = true, MaxConcurrent = 3 });
        using var session = new HeadlessSession(new ScriptedStreamingClient(), settings: settings);
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/agents", CancellationToken.None);

        var document = session.Observer.Events.OfType<DocumentWritten>().Single().Document;
        Assert.Contains(document.Blocks[0].Rows, row => row[0] == "Agents" && row[1] == "On");
        Assert.Contains(document.Blocks[0].Rows, row => row[0] == "Concurrency" && row[1] == "3");
        var table = document.Blocks.Single(block => block.Kind == "table");
        Assert.Equal(["Name", "Model", "Description"], table.Columns);
        Assert.Contains(table.Rows, row => row[0] == "explore" && row[1] == "Inherit");
        Assert.DoesNotContain(session.Observer.Events.OfType<NoteWritten>(), note => note.Text.Contains("inherit/inherit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentsBusy_CanListButCannotChangeSettings()
    {
        var client = new BlockingStreamingClient();
        using var session = new HeadlessSession(client);
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("busy", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await session.Session.SubmitAsync("/agents", CancellationToken.None);
        await session.Session.SubmitAsync("/agents on", CancellationToken.None);
        var listing = session.Observer.Events.OfType<DocumentWritten>().Single().Document;
        Assert.StartsWith("Agents", listing.Title, StringComparison.Ordinal);
        Assert.Contains(listing.Blocks[0].Rows, row => row[0] == "Agents" && row[1] == "Off");
        Assert.Contains(session.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("cannot change", StringComparison.Ordinal));
        session.Session.TryInterrupt();
        await session.Session.FinishTurnAsync();
    }
}
