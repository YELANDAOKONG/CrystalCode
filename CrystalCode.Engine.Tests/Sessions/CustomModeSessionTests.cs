using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CustomModeSessionTests
{
    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatUsageReceived(new TokenUsage(10, 2, 1)),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        new ChatToolCallDelta(0, 0, callId, name, arguments, ToolInputKind.Json),
        new ChatUsageReceived(new TokenUsage(10, 2, 1)),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];

    private static void Prepare(CrystalHome home, string workspace, string name, string tools, string prompt = "AUDIT BODY {{mode}}")
    {
        var directory = Path.Combine(home.Root, "modes", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "mode.json"),
            $$"""{"description":"Audit code","enabled":true,"tools":{{tools}}}""");
        File.WriteAllText(Path.Combine(directory, "prompt.md"), prompt);
    }

    [Fact]
    public async Task CustomMode_ExposesOnlyListedToolsAndItsOwnPrompt()
    {
        var client = new ScriptedStreamingClient(TextRound("ok"));
        using var headless = new HeadlessSession(
            client,
            prepare: (home, workspace) => Prepare(home, workspace, "audit", """["read","grep"]"""));
        await headless.Session.StartAsync(CancellationToken.None);

        Assert.True(headless.Session.TrySetMode("audit", out _));
        Assert.Equal("audit", headless.Session.CustomMode);
        Assert.False(headless.Session.PlanMode);
        await headless.RunTurnAsync("look");

        var request = client.LastRequest!;
        Assert.Equal(["read", "grep"], request.Tools.Select(tool => tool.Name).Order().Reverse());
        var system = request.Items.OfType<ChatMessage>().First(message => message.Role == ChatRole.System).Text;
        Assert.Contains("AUDIT BODY audit", system, StringComparison.Ordinal);
        Assert.Contains("<env>", system, StringComparison.Ordinal);
        Assert.DoesNotContain("coding agent running", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomMode_ListingBashDoesNotSkipApproval()
    {
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "bash", """{"command":"echo hi"}"""),
            TextRound("done"));
        using var headless = new HeadlessSession(
            client,
            prepare: (home, workspace) => Prepare(home, workspace, "ops", """["read","bash"]"""));
        await headless.Session.StartAsync(CancellationToken.None);
        Assert.True(headless.Session.TrySetMode("ops", out _));

        await headless.RunTurnAsync("run it");

        Assert.Equal(1, headless.Approvals.Count);
    }

    [Fact]
    public async Task CustomMode_UnlistedToolCallIsNotRegistered()
    {
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "bash", """{"command":"echo hi"}"""),
            TextRound("done"));
        using var headless = new HeadlessSession(
            client,
            prepare: (home, workspace) => Prepare(home, workspace, "audit", """["read"]"""));
        await headless.Session.StartAsync(CancellationToken.None);
        Assert.True(headless.Session.TrySetMode("audit", out _));

        await headless.RunTurnAsync("run it");

        Assert.Equal(0, headless.Approvals.Count);
        Assert.DoesNotContain(headless.Observer.Events.OfType<ErrorWritten>(), _ => true);
        var results = headless.Observer.Events.OfType<ToolResultsReceived>().Single().Results;
        Assert.Equal(ToolResultStatus.Failure, results.Single().Status);
        Assert.Equal(
            "Tool 'bash' is not available. Available tools: read.",
            results.Single().Text);
    }

    [Fact]
    public async Task CycleMode_WalksPlanWorkThenCustomModesAndWraps()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) =>
            {
                Prepare(home, workspace, "beta", """["read"]""");
                Prepare(home, workspace, "alpha", """["read"]""");
            });
        await headless.Session.StartAsync(CancellationToken.None);
        Assert.False(headless.Session.PlanMode);

        headless.Session.CycleMode();
        Assert.Equal((false, "alpha"), (headless.Session.PlanMode, headless.Session.CustomMode));
        headless.Session.CycleMode();
        Assert.Equal("beta", headless.Session.CustomMode);
        headless.Session.CycleMode();
        Assert.True(headless.Session.PlanMode);
        Assert.Null(headless.Session.CustomMode);
        headless.Session.CycleMode();
        Assert.False(headless.Session.PlanMode);
        Assert.Null(headless.Session.CustomMode);
    }

    [Fact]
    public async Task TogglePlan_LeavesACustomMode()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) => Prepare(home, workspace, "audit", """["read"]"""));
        await headless.Session.StartAsync(CancellationToken.None);
        Assert.True(headless.Session.TrySetMode("audit", out _));

        Assert.True(headless.Session.TogglePlan());

        Assert.Null(headless.Session.CustomMode);
    }

    [Fact]
    public async Task TrySetMode_RejectsUnknownName()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        Assert.False(headless.Session.TrySetMode("ghost", out var error));

        Assert.Contains("ghost", error, StringComparison.Ordinal);
        Assert.False(headless.Session.PlanMode);
    }

    [Fact]
    public async Task ModeCommand_SwitchesAndListsModes()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) => Prepare(home, workspace, "audit", """["read"]"""));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/mode", CancellationToken.None);
        await headless.Session.SubmitAsync("/mode audit", CancellationToken.None);
        await headless.Session.SubmitAsync("/mode ghost", CancellationToken.None);

        var notes = headless.Observer.Events.OfType<NoteWritten>().Select(note => note.Text).ToArray();
        Assert.Contains(notes, text => text.StartsWith("audit  Audit code", StringComparison.Ordinal));
        Assert.Contains("Mode  audit", notes);
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("ghost", StringComparison.Ordinal));
        Assert.Equal("audit", headless.Session.CustomMode);
    }

    [Fact]
    public async Task CustomMode_IsSavedAndRestoredOnResume()
    {
        var home = default(CrystalHome);
        SessionDocument saved;
        using (var first = new HeadlessSession(
                   new ScriptedStreamingClient(TextRound("ok")),
                   prepare: (h, w) =>
                   {
                       home = h;
                       Prepare(h, w, "audit", """["read"]""");
                   }))
        {
            await first.Session.StartAsync(CancellationToken.None);
            Assert.True(first.Session.TrySetMode("audit", out _));
            await first.RunTurnAsync("hello");
            saved = first.ReadSaved();
        }

        Assert.Equal("audit", saved.Mode);
        Assert.False(saved.PlanMode);

        using var second = new HeadlessSession(
            new ScriptedStreamingClient(),
            resume: saved,
            prepare: (h, w) => Prepare(h, w, "audit", """["read"]"""));
        await second.Session.StartAsync(CancellationToken.None);

        Assert.Equal("audit", second.Session.CustomMode);
        Assert.NotNull(home);
    }

    [Fact]
    public async Task Resume_MissingCustomModeFallsBackToWorkWithANote()
    {
        var document = new SessionDocument
        {
            Id = SessionStore.NewId(),
            Mode = "gone",
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hello" }]
        };
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), resume: document);

        await headless.Session.StartAsync(CancellationToken.None);

        Assert.Null(headless.Session.CustomMode);
        Assert.False(headless.Session.PlanMode);
        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text.Contains("'gone' is not available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChromeCarriesTheCustomModeName()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) => Prepare(home, workspace, "audit", """["read"]"""));
        await headless.Session.StartAsync(CancellationToken.None);

        headless.Session.TrySetMode("audit", out _);

        Assert.Equal("audit", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Mode);
    }
}
