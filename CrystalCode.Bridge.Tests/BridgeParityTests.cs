using System.Reflection;

using Crystal.Chat;

using CrystalCode.Bridge.Hosting;
using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>
/// Bridge must not silently lose an engine capability. These guards fail
/// when the engine or Crystal grows an event, a stream event, or a public
/// session member that Bridge neither maps nor lists with a reason.
/// </summary>
public sealed class BridgeParityTests
{
    /// <summary>Each public <see cref="CodingSession"/> member, and how Bridge reaches it or why it does not.</summary>
    private static readonly Dictionary<string, string> SessionMembers = new(StringComparer.Ordinal)
    {
        ["Create"] = "SessionLaunch.CreateSession on session/new",
        ["StartAsync"] = "session/new",
        ["SubmitAsync"] = "session/prompt through SessionRunner",
        ["SubmitQuickPromptAsync"] = "Phase 4: _crystal/quickPrompts/send",
        ["CompleteTurnAsync"] = "SessionRunner",
        ["FinishTurnAsync"] = "SessionRunner shutdown",
        ["TryInterrupt"] = "session/cancel",
        ["TryRecall"] = "Phase 4: _crystal/recall",
        ["RecallPending"] = "Phase 4: _crystal/recall",
        ["TogglePlan"] = "Phase 4: _crystal/mode/togglePlan; /plan works as text",
        ["CycleMode"] = "Phase 4: _crystal/mode/cycle",
        ["TrySetMode"] = "Launch mode on session/new; Phase 2: session/set_mode",
        ["SetVerbose"] = "Phase 4: _crystal/verbose",
        ["NotifyDraftChanged"] = "Phase 4: _crystal/draft",
        ["CloseAsync"] = "session/close and shutdown",
        ["Close"] = "CloseAsync covers it",
        ["TrySetDirectoryAccess"] = "/access as prompt text",
        ["PasteClipboardImageAsync"] = "Excluded: reads this machine's clipboard; image blocks instead",
        ["TryAttachImage"] = "image content blocks",
        ["TryAttachImageFile"] = "file:// image resource links",
        ["TryGetImage"] = "Phase 4: _crystal/image/get",
        ["TryExport"] = "Phase 4: _crystal/export",
        ["TryCancelSideQuestion"] = "Phase 4: _crystal/side/cancel",
        ["ClearSideQuestions"] = "Phase 4: _crystal/side/clear",
        ["Enqueue"] = "SessionRunner uses EnqueueAsync",
        ["EnqueueAsync"] = "SessionRunner",
        ["BeginOrigin"] = "SessionRunner per prompt",
        ["CurrentOrigin"] = "Not needed: one client per process",
        ["AgentsEnabled"] = "Phase 2: config options",
        ["TurnActive"] = "session/prompt follow-up handling",
        ["TurnTask"] = "SessionRunner and fault detection",
        ["PlanMode"] = "Phase 2: config options",
        ["CustomMode"] = "Phase 2: config options",
        ["SessionId"] = "The ACP session id"
    };

    [Fact]
    public void EveryPublicSessionMember_IsMappedOrExcluded()
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
    public void EverySessionEvent_IsMappedOrExcluded()
    {
        var events = typeof(SessionEvent).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(SessionEvent)) && !type.IsAbstract)
            .ToArray();

        var unmapped = events
            .Where(static type => !SessionUpdates.Mapped.Contains(type) && !SessionUpdates.Unmapped.ContainsKey(type))
            .Select(static type => type.Name)
            .ToArray();
        Assert.True(unmapped.Length == 0, "Unmapped session events: " + string.Join(", ", unmapped));
        Assert.Empty(SessionUpdates.Mapped.Intersect(SessionUpdates.Unmapped.Keys));
    }

    [Fact]
    public void EveryStreamEvent_IsMappedOrExcluded()
    {
        var events = typeof(ChatStreamEvent).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(ChatStreamEvent)) && !type.IsAbstract);

        var unmapped = events.Where(static type => !SessionUpdates.StreamEvents.ContainsKey(type)).Select(static type => type.Name).ToArray();
        Assert.True(unmapped.Length == 0, "Unmapped stream events: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void EveryFrontEndContract_IsImplemented()
    {
        var contracts = typeof(SessionFrontEnd).GetProperties()
            .Select(static property => property.PropertyType)
            .Where(static type => type.IsInterface && !type.IsGenericType);

        Assert.All(contracts, contract => Assert.True(contract.IsAssignableFrom(typeof(BridgeSession)), contract.Name));
        Assert.NotNull(typeof(BridgeSession).GetMethod(nameof(BridgeSession.ChooseChildAsync)));
    }

    [Fact]
    public void Bridge_ReferencesTheEngineOnly()
    {
        var referenced = typeof(CrystalBridge).Assembly.GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("CrystalCode.Engine", referenced);
        Assert.DoesNotContain("CrystalCode.Display", referenced);
        Assert.DoesNotContain("CrystalCode.Server", referenced);
        Assert.DoesNotContain("CrystalCode", referenced);
        Assert.DoesNotContain("Spectre.Console", referenced);
        Assert.DoesNotContain("Terminal.Gui", referenced);
        Assert.DoesNotContain("System.Console", referenced);
    }

    [Fact]
    public async Task LiteralApiKey_NeverReachesStdoutOrStderr()
    {
        const string secret = "sk-literal-secret-should-never-leak-1234";
        await using var fixture = await BridgeFixture.StartAsync(
            new ScriptedModel(false),
            options: static launch => launch with { Provider = "leaky", Model = "m" },
            prepare: static (home, _) => File.WriteAllText(
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
        var id = await fixture.OpenAsync(new { session = new { notices = new { } } });

        foreach (var command in new[] { "/tools", "/status full", "/stats", "/help", "/model", "/mcp", "hello" })
        {
            await fixture.RequestAsync("session/prompt", new { sessionId = id, prompt = new[] { new { type = "text", text = command } } });
        }

        lock (fixture.Lines)
        {
            Assert.All(fixture.Lines, static line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(secret, fixture.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StopReasons_MapToAcp()
    {
        Assert.Equal("end_turn", TurnWire.StopReason(TurnStopReason.Completed));
        Assert.Equal("max_tokens", TurnWire.StopReason(TurnStopReason.OutputTruncated));
        Assert.Equal("max_turn_requests", TurnWire.StopReason(TurnStopReason.ModelCallLimitReached));
        Assert.Equal("max_turn_requests", TurnWire.StopReason(TurnStopReason.ToolCallLimitReached));
        Assert.Equal("max_turn_requests", TurnWire.StopReason(TurnStopReason.DurationLimitReached));
        Assert.Equal("refusal", TurnWire.StopReason(TurnStopReason.ContentFiltered));
        Assert.Equal("cancelled", TurnWire.StopReason(TurnStopReason.Interrupted));
        Assert.Null(TurnWire.StopReason(TurnStopReason.Failed));
        Assert.Null(TurnWire.StopReason(TurnStopReason.ContextOverflow));
    }

    [Fact]
    public void OpaqueReasoningState_IsNeverSent()
    {
        var sent = new List<string>();
        var updates = new SessionUpdates(write => sent.Add(System.Text.Encoding.UTF8.GetString(Transport.JsonText.Write(write))), static () => null, notices: true);
        var state = new Crystal.Reasoning.OpaqueReasoningState("vendor.format", "opaque-bytes"u8.ToArray());

        updates.Apply(new StreamReceived(new ChatReasoningStateReceived(0, 0, state)));

        Assert.Empty(sent);
    }
}
