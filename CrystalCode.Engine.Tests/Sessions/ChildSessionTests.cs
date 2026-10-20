using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed partial class ChildSessionTests
{
    private static HarnessSettings Settings(int concurrency = 4) => HeadlessSession.ScriptedSettings()
        .WithAgents(new AgentSettings { Enabled = true, MaxConcurrent = concurrency });

    private static IReadOnlyList<ChatStreamEvent> Text(string text) =>
    [new ChatTextDelta(0, 0, ChatRole.Assistant, text), new ChatUsageReceived(new TokenUsage(10, 2, 1)), new ChatCandidateCompleted(0, FinishReason.Stop)];

    private static IReadOnlyList<ChatStreamEvent> Tool(string name, string arguments, string id = "call") =>
    [new ChatToolCallDelta(0, 0, id, name, arguments, ToolInputKind.Json), new ChatUsageReceived(new TokenUsage(20, 3, 1)),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)];

    [Theory]
    [InlineData("general", false, "work")]
    [InlineData("explore", false, "plan")]
    [InlineData("work", false, "work")]
    [InlineData("general", true, "plan")]
    [InlineData("explore", true, "plan")]
    public async Task BuiltInAgent_UsesResolvedOverridesAttachmentsAndChildPlaceholders(string agent, bool plan, string promptMode)
    {
        var arguments = JsonSerializer.Serialize(new { agent, label = "prompt test", prompt = "inspect" });
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", arguments), Text("child"), Text("parent")), settings: Settings(),
            prepare: (home, workspace) =>
            {
                var projectPrompts = Path.Combine(workspace, ".crystal", "prompts");
                Directory.CreateDirectory(home.PromptsDirectory);
                Directory.CreateDirectory(projectPrompts);
                var attachment = Path.Combine(home.PromptAttachmentsDirectory, "rules");
                Directory.CreateDirectory(attachment);
                File.WriteAllText(Path.Combine(attachment, "prompt.json"), "{\"enabled\":true}");
                foreach (var mode in new[] { "work", "plan" })
                {
                    File.WriteAllText(Path.Combine(home.PromptsDirectory, mode + ".md"), "HOME OVERRIDE " + mode);
                    File.WriteAllText(Path.Combine(projectPrompts, mode + ".md"), "PROJECT OVERRIDE " + mode + " {{session_id}} {{mode}}");
                    File.WriteAllText(Path.Combine(attachment, mode + ".md"), "ATTACHMENT " + mode + " {{session_id}}");
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
        Assert.Contains("PROJECT OVERRIDE " + promptMode, system, StringComparison.Ordinal);
        Assert.Contains("ATTACHMENT " + promptMode + " " + child.Id, system, StringComparison.Ordinal);
        Assert.Contains("WORKSPACE RULE", system, StringComparison.Ordinal);
        Assert.Contains("You are a child Agent", system, StringComparison.Ordinal);
        Assert.DoesNotContain("HOME OVERRIDE", system, StringComparison.Ordinal);
        Assert.DoesNotContain("{{session_id}}", system, StringComparison.Ordinal);
        Assert.DoesNotContain("{{mode}}", system, StringComparison.Ordinal);
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
}
