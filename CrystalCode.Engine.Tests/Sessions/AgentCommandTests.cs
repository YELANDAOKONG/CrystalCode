using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
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
    public async Task AgentsBusy_CanListButCannotChangeSettings()
    {
        var client = new BlockingStreamingClient();
        using var session = new HeadlessSession(client);
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("busy", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await session.Session.SubmitAsync("/agents", CancellationToken.None);
        await session.Session.SubmitAsync("/agents on", CancellationToken.None);
        Assert.Contains(session.Observer.Events.OfType<NoteWritten>(), note => note.Text.StartsWith("Agents off", StringComparison.Ordinal));
        Assert.Contains(session.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("cannot change", StringComparison.Ordinal));
        session.Session.TryInterrupt();
        await session.Session.FinishTurnAsync();
    }
}
