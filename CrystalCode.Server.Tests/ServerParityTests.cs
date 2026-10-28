using System.Reflection;
using System.Text.Json;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Endpoints;
using CrystalCode.Server.Hosting;
using CrystalCode.Server.Interactions;
using CrystalCode.Server.Security;
using CrystalCode.Server.Tests.Support;
using CrystalCode.Server.Wire;

using Xunit;

namespace CrystalCode.Server.Tests;

/// <summary>
/// The server must not lose an engine capability. These guards fail when
/// the engine or Crystal grows a public operation, an event, a stream event,
/// or a slash verb that the server does not map.
/// </summary>
public sealed class ServerParityTests
{
    /// <summary>
    /// Each public <see cref="CodingSession"/> member a front end may call, and
    /// the route or mechanism that reaches it. An entry without a route names
    /// why the server does not expose it.
    /// </summary>
    private static readonly Dictionary<string, string> SessionMembers = new(StringComparer.Ordinal)
    {
        ["Create"] = "SessionLaunch.CreateSession at startup",
        ["StartAsync"] = "ServerSession.RunAsync",
        ["SubmitAsync"] = "POST /input",
        ["SubmitQuickPromptAsync"] = "POST /quick-prompts/{name}/send",
        ["CompleteTurnAsync"] = "SessionRunner",
        ["FinishTurnAsync"] = "SessionRunner shutdown",
        ["TryInterrupt"] = "POST /interrupt",
        ["TryRecall"] = "POST /recall",
        ["RecallPending"] = "Recall state is part of POST /recall and message.recalled",
        ["TogglePlan"] = "POST /mode toggle-plan",
        ["CycleMode"] = "POST /mode cycle",
        ["TrySetMode"] = "POST /mode set",
        ["SetVerbose"] = "POST /verbose",
        ["NotifyDraftChanged"] = "PUT /draft",
        ["CloseAsync"] = "ServerSession shutdown",
        ["Close"] = "CloseAsync covers it",
        ["TrySetDirectoryAccess"] = "/access through POST /input",
        ["PasteClipboardImageAsync"] = "Excluded: reads the server's clipboard; POST /images instead",
        ["TryAttachImage"] = "POST /images",
        ["TryAttachImageFile"] = "/attach <path> through POST /input",
        ["TryGetImage"] = "GET /images/{number}",
        ["TryExport"] = "GET /export",
        ["TryCancelSideQuestion"] = "POST /side/cancel",
        ["ClearSideQuestions"] = "POST /side/clear",
        ["Enqueue"] = "SessionRunner uses EnqueueAsync",
        ["EnqueueAsync"] = "SessionRunner",
        ["BeginOrigin"] = "SessionRunner per request",
        ["CurrentOrigin"] = "InteractionBroker",
        ["AgentsEnabled"] = "chrome.agentsEnabled",
        ["TurnActive"] = "snapshot.turnActive",
        ["TurnTask"] = "SessionRunner",
        ["PlanMode"] = "chrome.planMode",
        ["CustomMode"] = "chrome.mode",
        ["SessionId"] = "envelope sessionId"
    };

    [Fact]
    public void EveryPublicSessionMember_IsMapped()
    {
        var members = typeof(CodingSession)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static member => member is MethodInfo { IsSpecialName: false } or PropertyInfo)
            .Select(static member => member.Name)
            .Where(static name => name is not ("Equals" or "GetHashCode" or "ToString" or "GetType"))
            .Distinct()
            .ToArray();

        var unmapped = members.Where(name => !SessionMembers.ContainsKey(name)).ToArray();
        Assert.True(unmapped.Length == 0, "Unmapped CodingSession members: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void EveryFrontEndContract_IsImplemented()
    {
        var properties = typeof(SessionFrontEnd).GetProperties().Select(static property => property.PropertyType).ToArray();
        var broker = typeof(InteractionBroker);

        Assert.All(
            properties.Where(static type => type.IsInterface && !type.IsGenericType && type != typeof(ISessionObserver)),
            contract => Assert.True(contract.IsAssignableFrom(broker), contract.Name));
        Assert.Contains(typeof(ISessionObserver), properties);
        Assert.True(typeof(ISessionObserver).IsAssignableFrom(typeof(Events.EventHub)));
        Assert.NotNull(broker.GetMethod(nameof(InteractionBroker.ChooseChildAsync)));
    }

    [Fact]
    public void EverySessionEvent_HasAWireType()
    {
        var events = typeof(SessionEvent).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(SessionEvent)) && !type.IsAbstract);

        var unmapped = events.Where(static type => !WireEvents.SessionTypes.ContainsKey(type)).Select(static type => type.Name).ToArray();
        Assert.True(unmapped.Length == 0, "Unmapped session events: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void EveryStreamEvent_HasAWireType()
    {
        var events = typeof(ChatStreamEvent).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(ChatStreamEvent)) && !type.IsAbstract);

        var unmapped = events.Where(static type => !WireEvents.StreamTypes.ContainsKey(type)).Select(static type => type.Name).ToArray();
        Assert.True(unmapped.Length == 0, "Unmapped stream events: " + string.Join(", ", unmapped));
    }

    /// <summary>
    /// Front ends probe these <c>GET /info</c> fields instead of a version.
    /// Renaming or removing one silently breaks them, so the names are fixed
    /// here and listed in docs/server.md. Adding a field is allowed.
    /// </summary>
    [Fact]
    public async Task Info_KeepsTheFieldNamesFrontEndsProbe()
    {
        await using var fixture = await ServerFixture.StartAsync(new ScriptedModel(false));
        await fixture.WaitReadyAsync();

        var info = await fixture.GetAsync(fixture.Http, "/info");

        string[] contract =
        [
            "product", "build", "state", "scope", "authentication",
            "operations", "eventTypes", "interactionKinds", "slashCommands"
        ];
        foreach (var name in contract)
        {
            Assert.True(info.TryGetProperty(name, out _), "GET /info lost the field " + name);
        }

        foreach (var name in new[] { "operations", "eventTypes", "interactionKinds", "slashCommands" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Array, info.GetProperty(name).ValueKind);
        }
    }

    [Fact]
    public void Info_ListsEveryRoute()
    {
        Assert.Equal(26, ServerEndpoints.Operations.Count);
        Assert.Contains("POST /recall", ServerEndpoints.Operations);
    }

    [Fact]
    public async Task LiteralApiKey_NeverReachesTheWire()
    {
        const string secret = "sk-literal-secret-should-never-leak-1234";
        await using var fixture = await ServerFixture.StartAsync(
            new ScriptedModel(false),
            prepare: (home, _) => File.WriteAllText(
                Path.Combine(home.Root, "providers.json"),
                $$"""
                {
                  "leaky": {
                    "protocol": "ollama",
                    "baseUri": "http://127.0.0.1:9/",
                    "apiKey": "{{secret}}",
                    "models": { "m": { "contextWindow": 8192 } }
                  }
                }
                """));
        await fixture.WaitReadyAsync();
        using var events = await fixture.OpenEventsAsync(fixture.Http);

        foreach (var command in new[] { "/tools", "/status full", "/stats", "/help", "/model", "/todos" })
        {
            await fixture.PostAsync(fixture.Http, "/input", new { text = command });
        }

        await events.WaitForAsync("report.help");
        var snapshot = await fixture.Http.GetStringAsync("/snapshot");
        var info = await fixture.Http.GetStringAsync("/info");
        lock (events.Seen)
        {
            Assert.All(events.Seen, value => Assert.DoesNotContain(secret, value.GetRawText(), StringComparison.Ordinal));
        }

        Assert.DoesNotContain(secret, snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, info, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_DoesNotReferenceTheTerminal()
    {
        var referenced = typeof(CrystalServer).Assembly.GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("CrystalCode.Engine", referenced);
        Assert.DoesNotContain("CrystalCode.Display", referenced);
        Assert.DoesNotContain("CrystalCode", referenced);
        Assert.DoesNotContain("Spectre.Console", referenced);
        Assert.DoesNotContain("Terminal.Gui", referenced);
        Assert.DoesNotContain("System.Console", referenced);
    }

    [Fact]
    public void WireJson_UsesNoVersionIdentifier()
    {
        Assert.All(ServerEndpoints.Operations, route => Assert.DoesNotMatch(@"/v\d", route));
        Assert.All(WireEvents.SessionTypes.Values, type => Assert.DoesNotMatch(@"v\d", type));
        var envelope = Json.Write(writer => WireEvents.Data(writer, new TurnStarted()));
        Assert.DoesNotContain("version", envelope, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpaqueReasoningState_IsNotSent()
    {
        var state = new Crystal.Reasoning.OpaqueReasoningState("vendor.format", "opaque-bytes"u8.ToArray());
        var text = Json.Write(writer => WireEvents.Data(writer,
            new StreamReceived(new ChatReasoningStateReceived(0, 0, state))));

        Assert.Contains("vendor.format", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String("opaque-bytes"u8.ToArray()), text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        Assert.False(document.RootElement.GetProperty("data").TryGetProperty("data", out _));
    }

    [Fact]
    public void ImageMarkers_AreSentAsPlainMarkersWithNumbers()
    {
        var text = Json.Write(writer => WireEvents.Data(writer, new UserMessageSent("look " + ImageMarkerText.Tag(3))));
        using var document = JsonDocument.Parse(text);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("look [Image #3]", data.GetProperty("text").GetString());
        Assert.Equal(3, data.GetProperty("images")[0].GetInt32());
    }

    [Fact]
    public void LoadingProgressed_WritesKindAndName()
    {
        var text = Json.Write(writer => WireEvents.Data(writer, new LoadingProgressed(LoadingKind.McpServer, "docs")));
        using var document = JsonDocument.Parse(text);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("loading.progressed", WireEvents.Type(new LoadingProgressed(LoadingKind.Plugin, "p")));
        Assert.Equal("mcp_server", data.GetProperty("kind").GetString());
        Assert.Equal("docs", data.GetProperty("name").GetString());
    }

    [Fact]
    public void Usage_WritesTotals()
    {
        var text = Json.Write(writer => WireWriter.Usage(writer, "usage", new TokenUsage(3, 4, 1)));
        using var document = JsonDocument.Parse(text);

        Assert.Equal(7, document.RootElement.GetProperty("usage").GetProperty("totalTokens").GetInt64());
    }
}
