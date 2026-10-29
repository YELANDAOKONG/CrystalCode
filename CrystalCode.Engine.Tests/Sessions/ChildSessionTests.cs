using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ChildSessionTests
{
    private static HarnessSettings Settings(int concurrency = 4) => HeadlessSession.ScriptedSettings()
        .WithAgents(new AgentSettings { Enabled = true, MaxConcurrent = concurrency });

    private static IReadOnlyList<ChatStreamEvent> Text(string text) =>
    [new ChatTextDelta(0, 0, ChatRole.Assistant, text), new ChatUsageReceived(new TokenUsage(10, 2, 1)), new ChatCandidateCompleted(0, FinishReason.Stop)];

    private static IReadOnlyList<ChatStreamEvent> Tool(string name, string arguments, string id = "call") =>
    [new ChatToolCallDelta(0, 0, id, name, arguments, ToolInputKind.Json), new ChatUsageReceived(new TokenUsage(20, 3, 1)),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)];

    [Theory]
    [InlineData("general", false, "general")]
    [InlineData("explore", false, "plan")]
    [InlineData("work", false, "work")]
    [InlineData("general", true, "plan")]
    [InlineData("explore", true, "plan")]
    public async Task BuiltInAgent_UsesResolvedOverridesAttachmentsAndChildPlaceholders(string agent, bool plan, string promptName)
    {
        var arguments = JsonSerializer.Serialize(new { agent, label = "prompt test", prompt = "inspect" });
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", arguments), Text("child"), Text("parent")), settings: Settings(),
            prepare: (home, workspace) =>
            {
                var projectPrompts = Path.Combine(workspace, ".crystal", "prompts");
                var homeAgents = Path.Combine(home.PromptsDirectory, "agents");
                var projectAgents = Path.Combine(projectPrompts, "agents");
                Directory.CreateDirectory(homeAgents);
                Directory.CreateDirectory(projectAgents);
                var attachment = Path.Combine(home.PromptAttachmentsDirectory, "rules");
                var attachmentAgents = Path.Combine(attachment, "agents");
                Directory.CreateDirectory(attachmentAgents);
                File.WriteAllText(Path.Combine(attachment, "prompt.json"), "{\"enabled\":true}");
                foreach (var name in new[] { "general", "work", "plan" })
                {
                    File.WriteAllText(Path.Combine(homeAgents, name + ".md"), "HOME AGENT " + name);
                    File.WriteAllText(
                        Path.Combine(projectAgents, name + ".md"),
                        "PROJECT AGENT " + name + " {{session_id}} {{mode}}");
                    File.WriteAllText(
                        Path.Combine(attachmentAgents, name + ".md"),
                        "ATTACHMENT AGENT " + name + " {{session_id}}");
                }

                foreach (var mode in new[] { "work", "plan" })
                {
                    File.WriteAllText(Path.Combine(home.PromptsDirectory, mode + ".md"), "PARENT OVERRIDE " + mode);
                    File.WriteAllText(Path.Combine(projectPrompts, mode + ".md"), "PARENT PROJECT " + mode);
                    File.WriteAllText(Path.Combine(attachment, mode + ".md"), "PARENT ATTACHMENT " + mode);
                }

                File.WriteAllText(Path.Combine(workspace, "AGENTS.md"), "WORKSPACE RULE");
            });
        await session.Session.StartAsync(CancellationToken.None);
        if (plan)
        {
            session.Session.TogglePlan();
        }
        await session.RunTurnAsync("delegate");
        var child = Assert.Single(session.ReadSaved().Children);
        var system = Assert.Single(child.Items, item => item.Role == "system").Text!;
        Assert.Contains("PROJECT AGENT " + promptName, system, StringComparison.Ordinal);
        Assert.Contains("ATTACHMENT AGENT " + promptName + " " + child.Id, system, StringComparison.Ordinal);
        Assert.Contains("WORKSPACE RULE", system, StringComparison.Ordinal);
        Assert.Contains("You are a child Agent", system, StringComparison.Ordinal);
        Assert.DoesNotContain("HOME AGENT", system, StringComparison.Ordinal);
        Assert.DoesNotContain("PARENT OVERRIDE", system, StringComparison.Ordinal);
        Assert.DoesNotContain("PARENT PROJECT", system, StringComparison.Ordinal);
        Assert.DoesNotContain("PARENT ATTACHMENT", system, StringComparison.Ordinal);
        Assert.DoesNotContain("{{session_id}}", system, StringComparison.Ordinal);
        Assert.DoesNotContain("{{mode}}", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomAgent_IgnoresPromptOverlaysSetsAndAttachments()
    {
        var arguments = JsonSerializer.Serialize(new { agent = "reviewer", label = "custom", prompt = "inspect" });
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", arguments), Text("child"), Text("parent")), settings: Settings(),
            prepare: (home, workspace) =>
            {
                var agentDirectory = Path.Combine(home.Root, "agents", "reviewer");
                Directory.CreateDirectory(agentDirectory);
                File.WriteAllText(
                    Path.Combine(agentDirectory, "agent.json"),
                    "{\"description\":\"Custom reviewer.\",\"enabled\":true}");
                File.WriteAllText(Path.Combine(agentDirectory, "prompt.md"), "CUSTOM ROLE {{session_id}}");
                var projectAgents = Path.Combine(workspace, ".crystal", "prompts", "agents");
                Directory.CreateDirectory(projectAgents);
                File.WriteAllText(Path.Combine(projectAgents, "general.md"), "PROJECT AGENT general");
                Directory.CreateDirectory(home.PromptsDirectory);
                File.WriteAllText(Path.Combine(home.PromptsDirectory, "work.md"), "PARENT WORK");
                var attachment = Path.Combine(home.PromptAttachmentsDirectory, "rules");
                Directory.CreateDirectory(Path.Combine(attachment, "agents"));
                File.WriteAllText(Path.Combine(attachment, "prompt.json"), "{\"enabled\":true}");
                File.WriteAllText(Path.Combine(attachment, "agents", "general.md"), "ATTACHMENT AGENT general");
            });
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var child = Assert.Single(session.ReadSaved().Children);
        var system = Assert.Single(child.Items, item => item.Role == "system").Text!;
        Assert.Contains("CUSTOM ROLE " + child.Id, system, StringComparison.Ordinal);
        Assert.Contains("You are a child Agent", system, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT AGENT", system, StringComparison.Ordinal);
        Assert.DoesNotContain("ATTACHMENT AGENT", system, StringComparison.Ordinal);
        Assert.DoesNotContain("PARENT WORK", system, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentGeneralPrompt.Text, system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_IsAbsentByDefaultAndCannotBeInvoked()
    {
        var model = new ScriptedStreamingClient(Text("done"));
        using var session = new HeadlessSession(model);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("hello");
        Assert.DoesNotContain(model.LastRequest!.Tools, tool => tool.Name == "task");
        Assert.Empty(session.ReadSaved().Children);
    }

    [Theory]
    [InlineData(null, null, null, null, null, 100_000)]
    [InlineData("priority", "fast", 200_000, "priority", "fast", 200_000)]
    public async Task BuiltInAgent_WithItsOwnModel_IgnoresTheSessionTierSpeedAndContext(
        string? tier,
        string? speed,
        int? context,
        string? expectedTier,
        string? expectedSpeed,
        int expectedContext)
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            {
                ["model"] = new(200_000),
                ["child"] = new(200_000, contextWindows: [100_000, 200_000],
                    serviceTiers: ["flex", "priority"], speeds: ["fast", "slow"])
            }, requiresApiKey: false);
        var settings = new HarnessSettings(provider.Name, "model", CrystalCode.Engine.Approvals.ApprovalMode.Default, 0.8,
                ProviderCatalog.CreateStarter().Overlay([provider]))
            .WithTier(provider.Name, "child", "flex")
            .WithSpeed(provider.Name, "child", "slow")
            .WithContextWindow(provider.Name, "child", 200_000)
            .WithAgents(new AgentSettings
            {
                Enabled = true,
                Explore = new AgentModelSettings
                {
                    Provider = "scripted", Model = "child", Tier = tier, Speed = speed, Context = context
                }
            });
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"look","prompt":"inspect"}"""), Text("child"), Text("parent")),
            settings: settings);
        await session.Session.StartAsync(CancellationToken.None);

        await session.RunTurnAsync("delegate");

        Assert.Contains(("child", expectedTier, expectedSpeed), session.ChatPlugin.Slots);
        Assert.DoesNotContain(session.ChatPlugin.Slots, slot => slot.Model == "child" && slot.ServiceTier == "flex");
        var child = Assert.Single(session.ReadSaved().Children);
        Assert.Equal(expectedContext, child.Model.Context);
        Assert.Equal(expectedTier, child.Model.Tier);
        Assert.Equal(expectedSpeed, child.Model.Speed);
    }

    [Fact]
    public async Task Task_CreatesIndependentChildAndAccountsUsageOnlyOnce()
    {
        var model = new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"find notes","prompt":"Read notes.txt"}"""),
            Tool("read", """{"path":"notes.txt"}"""), Text("The file says alpha."), Text("Parent report"));
        using var session = new HeadlessSession(model, settings: Settings());
        await File.WriteAllTextAsync(Path.Combine(session.WorkspacePath, "notes.txt"), "alpha");
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate reading");
        var saved = session.ReadSaved();
        var child = Assert.Single(saved.Children);
        Assert.Equal("completed", child.Status);
        Assert.DoesNotContain("task", child.Tools);
        Assert.DoesNotContain("question", child.Tools);
        Assert.DoesNotContain("bash", child.Tools);
        Assert.Equal(2, child.ModelCalls);
        Assert.Equal(30, child.CumulativeUsage!.InputTokenCount);
        Assert.Equal(60, saved.CumulativeUsage!.InputTokenCount);
        Assert.Equal(10, saved.Usage!.InputTokenCount);
        Assert.Equal(4, saved.ModelCalls);
        Assert.Single(saved.Archive!, item => item.Role == "user");
        Assert.DoesNotContain(saved.Archive!, item => item.Text == "The file says alpha.");
        Assert.Contains(session.Observer.Events.OfType<ChildEventReceived>(), item => item.ChildSessionId == child.Id && item.Event is StreamReceived);
        Assert.Single(new SessionStore(session.Home).List(session.WorkspacePath));
    }

    [Fact]
    public async Task Task_ResumeUsesSameChildAndPreservesHistory()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"first","prompt":"First question"}"""), Text("first answer"), Text("parent"),
            Text("unused")), settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("first delegation");
        var original = session.ReadSaved();
        var childId = Assert.Single(original.Children).Id;
        var arguments = JsonSerializer.Serialize(new { agent = "explore", label = "continue", prompt = "Second question", sessionId = childId });
        using var resumed = new HeadlessSession(new ScriptedStreamingClient(Tool("task", arguments), Text("second answer"), Text("parent")),
            resume: original, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue delegation");
        var saved = resumed.ReadSaved();
        var child = Assert.Single(saved.Children);
        Assert.Equal(childId, child.Id);
        Assert.Contains(child.Archive, item => item.Text == "first answer");
        Assert.Contains(child.Archive, item => item.Text == "second answer");
        Assert.Equal(20, child.CumulativeUsage!.InputTokenCount);
        Assert.Equal(80, saved.CumulativeUsage!.InputTokenCount);
    }

    [Fact]
    public async Task Task_SharedModelBudgetStopsChildAndParentTogether()
    {
        var model = new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"budget","prompt":"Read"}"""),
            Tool("read", """{"path":"missing.txt"}"""), Text("must not run"));
        using var session = new HeadlessSession(model, settings: Settings().WithExecutionBudget(new TurnLimits(2, 10, null)));
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        Assert.Equal(2, model.RequestCount);
        Assert.Equal("model_call_limit_reached", Assert.Single(session.ReadSaved().Children).StopReason);
        Assert.Equal(TurnStopReason.ModelCallLimitReached, session.Observer.Events.OfType<TurnFinished>().Last().Result.StopReason);
    }

    [Fact]
    public async Task Task_ConfiguredChildLimitStopsChildButNotParent()
    {
        var model = new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"limit","prompt":"Read"}"""),
            Tool("read", """{"path":"missing.txt"}"""), Text("parent done"));
        var settings = HeadlessSession.ScriptedSettings().WithAgents(
            new AgentSettings { Enabled = true, Limits = new AgentLimits(maximumModelCalls: 1) });
        using var session = new HeadlessSession(model, settings: settings);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        Assert.Equal("model_call_limit_reached", Assert.Single(session.ReadSaved().Children).StopReason);
        Assert.Equal(TurnStopReason.Completed, session.Observer.Events.OfType<TurnFinished>().Last().Result.StopReason);
    }

    [Fact]
    public async Task Task_PlanCannotLaunchWork()
    {
        var model = new ScriptedStreamingClient(Tool("task", """{"agent":"work","label":"bad","prompt":"edit"}"""), Text("blocked"));
        using var session = new HeadlessSession(model, settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        session.Session.TogglePlan();
        await session.RunTurnAsync("delegate");
        Assert.Empty(session.ReadSaved().Children);
        Assert.DoesNotContain("work:", model.LastRequest!.Tools.Single(tool => tool.Name == "task").Description);
        Assert.Equal(ToolResultStatus.Failure, session.Observer.Events.OfType<ToolResultsReceived>().First().Results[0].Status);
    }

    [Fact]
    public async Task Task_ExploreCannotExecuteEditEvenIfModelRequestsIt()
    {
        var model = new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"read only","prompt":"inspect"}"""),
            Tool("write", """{"path":"forbidden.txt","contents":"bad"}"""), Text("blocked"), Text("parent"));
        using var session = new HeadlessSession(model, settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        Assert.False(File.Exists(Path.Combine(session.WorkspacePath, "forbidden.txt")));
        var child = Assert.Single(session.ReadSaved().Children);
        Assert.Contains(child.Archive, item => item.Kind == "tool_result" && item.Status == "failure");
    }

    [Fact]
    public async Task Clear_DropsChildrenAndUsage()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"read","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        await session.Session.SubmitAsync("/clear", CancellationToken.None);
        Assert.Empty(session.ReadSaved().Children);
        Assert.Null(session.ReadSaved().CumulativeUsage);
    }

    [Fact]
    public async Task UnknownAgentModel_IsExcludedWithoutFallback()
    {
        var settings = Settings().WithAgents(new AgentSettings { Enabled = true,
            Explore = new AgentModelSettings { Provider = "scripted", Model = "missing" } });
        var model = new ScriptedStreamingClient(Text("done"));
        using var session = new HeadlessSession(model, settings: settings);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("hello");
        Assert.DoesNotContain("explore:", model.LastRequest!.Tools.Single(tool => tool.Name == "task").Description);
        Assert.Contains(session.Observer.Events.OfType<NoteWritten>(), note => note.Text.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VisionChild_ReceivesOnlyItsOwnImagesAndPersistsThemInParentMediaStore()
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            {
                ["model"] = new(200_000), ["vision"] = new(200_000, imageInput: true)
            });
        var settings = new HarnessSettings(provider.Name, "model", Engine.Approvals.ApprovalMode.Default, 0.8,
            new ProviderCatalog([provider]), agents: new AgentSettings { Enabled = true,
                Explore = new AgentModelSettings { Provider = "scripted", Model = "vision" } });
        var text = new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"look","prompt":"Inspect sample.png"}"""), Text("parent"));
        var vision = new ChildVisionClient();
        using var session = new HeadlessSession(text, settings: settings, images: vision);
        await File.WriteAllBytesAsync(Path.Combine(session.WorkspacePath, "sample.png"),
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate image inspection");
        var saved = session.ReadSaved();
        var child = Assert.Single(saved.Children);
        Assert.Equal("completed", child.Status);
        Assert.Equal("vision", child.Model.Model);
        Assert.Contains("view_image", child.Tools);
        Assert.DoesNotContain(text.LastRequest!.Tools, tool => tool.Name == "view_image");
        Assert.Equal(2, vision.Requests.Count);
        Assert.Contains(Assert.Single(vision.Requests[1].Items.OfType<MultimodalToolResult>()).Contents,
            content => content is ImageContent);
        Assert.Empty(saved.Images);
        var image = Assert.Single(child.Images);
        Assert.NotNull(image.ContentHash);
        Assert.Null(image.Data);
        Assert.Equal(30, child.CumulativeUsage!.InputTokenCount);
        Assert.Equal(60, saved.CumulativeUsage!.InputTokenCount);
        var exportPath = Path.Combine(session.WorkspacePath, "child-export.json");
        await session.Session.SubmitAsync("/export json \"" + exportPath + "\"", CancellationToken.None);
        using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(exportPath));
        var exportedImage = exported.RootElement.GetProperty("session").GetProperty("children")[0].GetProperty("images")[0];
        Assert.NotNull(exportedImage.GetProperty("data").GetString());
    }

    [Fact]
    public async Task TextChild_HasNoViewImageEvenWhenAudioAndVideoAreDeclared()
    {
        var provider = new ProviderDefinition(new ProviderName("scripted"), ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"), new Dictionary<string, ModelSettings>
            { ["model"] = new(200_000, audioInput: true, videoInput: true) });
        var settings = new HarnessSettings(provider.Name, "model", Engine.Approvals.ApprovalMode.Default, 0.8,
            new ProviderCatalog([provider]), agents: new AgentSettings { Enabled = true });
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"text","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: settings);
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        Assert.DoesNotContain("view_image", Assert.Single(session.ReadSaved().Children).Tools);
    }

    [Fact]
    public async Task ChildView_UsesUniquePrefixAndNeverSubmitsToTheChild()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"first","prompt":"inspect"}"""), Text("child"), Text("parent"), Text("follow up")),
            settings: Settings(), chooseChild: (children, _) => Task.FromResult<string?>(children[0].Id));
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var child = Assert.Single(session.ReadSaved().Children);
        await session.Session.SubmitAsync("/child", CancellationToken.None);
        Assert.Equal(child.Id, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
        await session.Session.SubmitAsync("/child " + child.Id[..8], CancellationToken.None);
        Assert.Equal(child.Id, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
        await session.RunTurnAsync("ordinary follow up");
        Assert.Equal(child.Archive.Length, Assert.Single(session.ReadSaved().Children).Archive.Length);
        await session.Session.SubmitAsync("/child parent", CancellationToken.None);
        Assert.Null(session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ParallelTasks_UseSeparateIdsAndRespectSessionConcurrency(int concurrency)
    {
        var parentRounds = 0;
        var started = 0;
        var active = 0;
        var maximum = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RoutingStreamingClient(async (request, token) =>
        {
            if (request.Tools.Any(tool => tool.Name == "task"))
            {
                if (Interlocked.Increment(ref parentRounds) > 1)
                {
                    return Text("parent report");
                }
                return Enumerable.Range(0, 3).Select(index => (ChatStreamEvent)new ChatToolCallDelta(0, index,
                    "task" + index, "task", JsonSerializer.Serialize(new { agent = "explore", label = "part" + index, prompt = "part" + index }),
                    ToolInputKind.Json)).Concat([new ChatUsageReceived(new TokenUsage(20, 3, 1)),
                        new ChatCandidateCompleted(0, FinishReason.ToolCalls)]).ToArray();
            }
            var count = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = maximum;
            }
            while (count > observed && Interlocked.CompareExchange(ref maximum, count, observed) != observed);
            if (Interlocked.Increment(ref started) == concurrency)
            {
                ready.TrySetResult();
            }
            try
            {
                await release.Task.WaitAsync(token);
                var question = request.Items.OfType<ChatMessage>().Last(message => message.Role == ChatRole.User).Text;
                return Text("answer:" + question);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        using var session = new HeadlessSession(client, settings: Settings(concurrency));
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate three independent tasks", CancellationToken.None);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(concurrency, Volatile.Read(ref active));
        release.SetResult();
        await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await session.Session.FinishTurnAsync();
        var saved = session.ReadSaved();
        Assert.Equal(3, saved.Children.Count);
        Assert.Equal(3, saved.Children.Select(child => child.Id).Distinct().Count());
        Assert.Equal(concurrency, maximum);
        Assert.Equal(60, saved.CumulativeUsage!.InputTokenCount);
        foreach (var child in saved.Children)
        {
            Assert.Equal("completed", child.Status);
            Assert.Contains(child.Archive, item => item.Text == "answer:" + child.Label);
            Assert.Single(child.Archive, item => item.Role == "assistant");
            Assert.DoesNotContain(child.Tools, name => name == "task");
        }
        var calls = session.Observer.Events.OfType<ToolCallsIssued>().Single().Calls;
        var results = session.Observer.Events.OfType<ToolResultsReceived>().Single().Results;
        Assert.Equal(calls.Select(call => call.CallId), results.Select(result => result.CallId));
    }

    [Fact]
    public async Task Cancel_StopsRunningAndQueuedChildrenWithoutStartingQueuedModels()
    {
        var parent = false;
        var childStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childRequests = 0;
        var client = new RoutingStreamingClient(async (request, token) =>
        {
            if (request.Tools.Any(tool => tool.Name == "task"))
            {
                Assert.False(parent);
                parent = true;
                return new ChatStreamEvent[]
                {
                    new ChatToolCallDelta(0, 0, "one", "task", """{"agent":"explore","label":"one","prompt":"one"}""", ToolInputKind.Json),
                    new ChatToolCallDelta(0, 1, "two", "task", """{"agent":"explore","label":"two","prompt":"two"}""", ToolInputKind.Json),
                    new ChatUsageReceived(new TokenUsage(20, 3, 1)), new ChatCandidateCompleted(0, FinishReason.ToolCalls)
                };
            }
            Interlocked.Increment(ref childRequests);
            childStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Text("unreachable");
        });
        using var session = new HeadlessSession(client, settings: Settings(1));
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await childStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.Session.TryInterrupt());
        await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await session.Session.FinishTurnAsync();
        Assert.Equal(1, childRequests);
        Assert.All(session.ReadSaved().Children, child => Assert.Equal("interrupted", child.Status));
    }

    [Fact]
    public async Task ResumeUnderPlan_RemovesSavedWriteTools()
    {
        using var first = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"general","label":"first","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: Settings());
        await first.Session.StartAsync(CancellationToken.None);
        await first.RunTurnAsync("delegate");
        var document = first.ReadSaved();
        var child = Assert.Single(document.Children);
        Assert.Contains("write", child.Tools);
        document.PlanMode = true;
        var args = JsonSerializer.Serialize(new { agent = "general", label = "continue", prompt = "write", sessionId = child.Id });
        var model = new ScriptedStreamingClient(Tool("task", args), Tool("write", """{"path":"bad.txt","contents":"bad"}"""),
            Text("blocked"), Text("parent"));
        using var resumed = new HeadlessSession(model, document, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.False(File.Exists(Path.Combine(resumed.WorkspacePath, "bad.txt")));
        Assert.Equal(child.Id, Assert.Single(resumed.ReadSaved().Children).Id);
    }

    [Fact]
    public async Task ChildCheckpoint_PersistsLiveParentToolResultsBeforeParentFinishes()
    {
        var rounds = 0;
        var parentWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RoutingStreamingClient(async (request, token) =>
        {
            if (!request.Tools.Any(tool => tool.Name == "task"))
            {
                return Text("child completed");
            }
            if (Interlocked.Increment(ref rounds) == 1)
            {
                return Tool("task", """{"agent":"explore","label":"checkpoint","prompt":"inspect"}""");
            }
            parentWaiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Text("unreachable");
        });
        using var session = new HeadlessSession(client, settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await parentWaiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var checkpoint = session.ReadSaved();
        var child = Assert.Single(checkpoint.Children);
        Assert.Equal(30, checkpoint.CumulativeUsage!.InputTokenCount);
        Assert.Equal(2, checkpoint.ModelCalls);
        var savedCall = Assert.Single(checkpoint.Items, item => item.Kind == "tool_call");
        var savedResult = Assert.Single(checkpoint.Items, item => item.Kind == "tool_result");
        Assert.Equal(savedCall.CallId, savedResult.CallId);
        Assert.Contains(child.Id, savedResult.Text, StringComparison.Ordinal);
        Assert.Contains("child completed", savedResult.Text, StringComparison.Ordinal);
        session.Session.TryInterrupt();
        await session.Session.FinishTurnAsync();
        using var resumed = new HeadlessSession(new ScriptedStreamingClient(Text("resumed parent")), checkpoint, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.Equal(40, resumed.ReadSaved().CumulativeUsage!.InputTokenCount);
        Assert.Equal(child.Id, Assert.Single(resumed.ReadSaved().Children).Id);
    }

    [Fact]
    public async Task Fork_CopiesChildrenWithoutSharingMutableTranscriptOrCountingUsageAgain()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"fork","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var source = session.ReadSaved();
        var fork = SessionFork.Create(source, "forked", session.WorkspacePath, DateTimeOffset.UtcNow);
        var original = Assert.Single(source.Children);
        var branched = Assert.Single(fork.Children);
        Assert.Equal(original.Id, branched.Id);
        Assert.Equal(source.CumulativeUsage!.InputTokenCount, fork.CumulativeUsage!.InputTokenCount);
        branched.Archive[0].Text = "changed branch";
        Assert.NotEqual("changed branch", original.Archive[0].Text);
        using var resumed = new HeadlessSession(new ScriptedStreamingClient(Text("next")), fork, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.Equal(50, resumed.ReadSaved().CumulativeUsage!.InputTokenCount);
    }

    [Fact]
    public async Task Task_RetriesChildUsingSessionRetryAndKeepsCallCountLogical()
    {
        var parents = 0;
        var children = 0;
        var client = new RoutingStreamingClient((request, _) =>
        {
            if (request.Tools.Any(tool => tool.Name == "task"))
            {
                return Task.FromResult(Interlocked.Increment(ref parents) == 1
                    ? Tool("task", """{"agent":"explore","label":"retry","prompt":"inspect"}""") : Text("parent"));
            }
            if (Interlocked.Increment(ref children) == 1)
            {
                throw new HttpRequestException("503 service unavailable");
            }
            return Task.FromResult(Text("child"));
        });
        using var session = new HeadlessSession(client, settings: Settings().WithRetry(new RetrySettings(initialDelaySeconds: 0)));
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var child = Assert.Single(session.ReadSaved().Children);
        Assert.Equal(2, children);
        Assert.Equal(1, child.ModelCalls);
        Assert.Contains(session.Observer.Events.OfType<ChildEventReceived>(), received => received.ChildSessionId == child.Id && received.Event is RetryScheduled);
        Assert.Equal(40, session.ReadSaved().CumulativeUsage!.InputTokenCount);
    }

    [Fact]
    public async Task ChildApproval_EmitsSourceScopedEventsAndKeepsPromptNotifications()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"read","prompt":"inspect"}"""),
            Tool("read", """{"path":"notes.txt"}"""), Text("child"), Text("parent")), settings: Settings());
        await File.WriteAllTextAsync(Path.Combine(session.WorkspacePath, "notes.txt"), "notes");
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");

        var child = Assert.Single(session.ReadSaved().Children);
        var approval = Assert.Single(session.Observer.Events.OfType<ChildEventReceived>(),
            item => item.Event is ApprovalPassed);
        Assert.Equal(child.Id, approval.ChildSessionId);
        var passed = Assert.IsType<ApprovalPassed>(approval.Event);
        Assert.Equal("read", passed.Call.Name);
        Assert.Contains(child.Id, passed.Classification.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(session.Observer.Events.OfType<ApprovalPassed>(), item => item.Call.Name == "read");
        Assert.True(session.Approvals.PassCount > 0);
    }

    [Fact]
    public async Task RunningChild_PublishesTodosAndSupportsAwaitedPumpNavigationWithoutInterrupting()
    {
        var model = new HeldStreamingClient(
            Tool("task", """{"agent":"general","label":"plan","prompt":"make a plan"}"""),
            Tool("todowrite", """{"todos":[{"id":"a","content":"child todo","status":"in_progress"}]}"""));
        ChildSessionDocument? picked = null;
        using var session = new HeadlessSession(model, settings: Settings(), chooseChild: (children, _) =>
        {
            picked = Assert.Single(children);
            return Task.FromResult<string?>(picked.Id);
        });
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await model.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var changed = Assert.Single(session.Observer.Events.OfType<ChildEventReceived>(),
                item => item.Event is TodosChanged);
            var todo = Assert.Single(Assert.IsType<TodosChanged>(changed.Event).Items);
            Assert.Equal("child todo", todo.Content);
            Assert.Equal(TodoStatus.InProgress, todo.Status);
            Assert.DoesNotContain(session.Observer.Events.OfType<TodosChanged>(),
                item => item.Items.Any(value => value.Content == "child todo"));

            await session.Session.EnqueueAsync("/child", CancellationToken.None);
            Assert.NotNull(picked);
            Assert.Equal("running", picked.Status);
            Assert.Equal("child todo", Assert.Single(picked.Todos).Content);
            Assert.Equal(changed.ChildSessionId, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
            await session.Session.EnqueueAsync("/child " + picked.Id[..8], CancellationToken.None);
            Assert.Equal(picked.Id, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
            await session.Session.EnqueueAsync("/child parent", CancellationToken.None);
            Assert.Null(session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
            await session.Session.EnqueueAsync("/agents", CancellationToken.None);
            Assert.DoesNotContain(session.Observer.Events.OfType<ErrorWritten>(),
                error => error.Text.Contains("Finish the current turn", StringComparison.Ordinal));
            Assert.True(session.Session.TurnActive);
            Assert.False(session.Session.TurnTask!.IsCompleted);
        }
        finally
        {
            session.Session.TryInterrupt();
            await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
            await session.Session.FinishTurnAsync();
        }
        var saved = Assert.Single(session.ReadSaved().Children);
        Assert.Equal("child todo", Assert.Single(saved.Todos).Content);
    }

    [Fact]
    public async Task ChildPicker_OpensWhileChildApprovalReviewIsPending()
    {
        var model = new GatedReviewStreamingClient(
            Tool("task", """{"agent":"work","label":"build","prompt":"run the build"}"""),
            Tool("bash", """{"command":"echo build"}"""), Text("child"), Text("parent"));
        var picked = 0;
        using var session = new HeadlessSession(model, settings: Settings().WithApproval(Engine.Approvals.ApprovalMode.Review),
            chooseChild: (_, _) =>
            {
                Interlocked.Increment(ref picked);
                return Task.FromResult<string?>(null);
            });
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await model.ReviewEntered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await session.Session.EnqueueAsync("/child", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, picked);
        }
        finally
        {
            model.Release("""{"outcome":"deny","risk_level":"low","user_authorization":"low","rationale":"test"}""");
            await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
            await session.Session.FinishTurnAsync();
        }
    }

    [Fact]
    public async Task PumpNavigation_AwaitsPickerAndPropagatesCancellation()
    {
        using var first = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"read","prompt":"inspect"}"""),
            Text("child"), Text("parent")), settings: Settings());
        await first.Session.StartAsync(CancellationToken.None);
        await first.RunTurnAsync("delegate");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new HeadlessSession(new ScriptedStreamingClient(Text("unused")),
            resume: first.ReadSaved(), settings: Settings(), chooseChild: async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return null;
            });
        await session.Session.StartAsync(CancellationToken.None);
        using var source = new CancellationTokenSource();
        var navigation = session.Session.EnqueueAsync("/child", source.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(navigation.IsCompleted);
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
        await session.Session.EnqueueAsync("/child parent", CancellationToken.None);
        Assert.Null(session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
    }
}
