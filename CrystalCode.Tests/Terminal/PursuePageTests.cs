using Spectre.Console;

using Crystal;
using Crystal.Chat;

using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Pursue;
using CrystalCode.Terminal;

using Xunit;
using Xunit.Abstractions;

namespace CrystalCode.Tests.Terminal;

[Collection(CrystalCode.Tests.Commands.ConsoleStateCollection.Name)]
public sealed class PursuePageTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(80, 24, "default")]
    [InlineData(100, 30, "dark")]
    [InlineData(120, 32, "light")]
    public void Paint_UsesTheThemeAndKeepsProgressAndNavigationPinned(int width, int height, string theme)
    {
        Assert.True(Theme.TryUseBuiltIn(theme));
        try
        {
            var page = new PursuePage();
            page.Update(State(Check("first")));
            var lines = page.Paint(width, height, Configuration(), PaintLine.Colored(Theme.Accent, "Checking Objective · 2s"));
            var text = string.Join('\n', lines.Select(line => line.Plain));
            output.WriteLine(text);
            Assert.Equal(height, lines.Count);
            Assert.All(lines, line => Assert.True(TextWidth.Measure(line.Plain) <= width));
            Assert.Contains("Continuations 2/1,000", text);
            Assert.Contains("Stalls 1/10", text);
            Assert.Contains("Checks 1", text);
            Assert.Contains("Evidence (1)", text);
            Assert.Contains("[4] Tool Result", text);
            Assert.Contains("Tests pass.", text);
            Assert.Contains("Checking Objective", lines[^2].Plain);
            Assert.Contains("Esc", lines[^1].Plain);
            Assert.Contains("Esc/Q/F9 back", lines[^1].Plain);
            Assert.Contains("L latest", lines[^1].Plain);
            Assert.Contains("P pause", lines[^1].Plain);
            Assert.Contains(Theme.Heading, lines[0].Markup);
            Assert.Contains(Theme.Accent, lines[3].Markup);
            Assert.Contains(Theme.Rule, lines[3].Markup);
            Assert.DoesNotContain("%", lines[3].Plain);
            foreach (var line in lines)
            {
                _ = new Markup(line.Markup);
            }
        }
        finally
        {
            Theme.TryUseBuiltIn("default");
        }
    }

    [Fact]
    public void Paint_ShowsWaitFailuresAndReachedBudgetsWithoutImplyingCompletion()
    {
        var page = new PursuePage();
        var waiting = Check("wait") with { Verdict = "wait", WaitFor = ["task-one", "task-two"] };
        page.Update(State(waiting) with { Phase = PursuePhase.Waiting, Continuations = 0, Stalls = 10 });
        var configuration = new PursueConfiguration(PursueModelSettings.Default, PursueLimits.Default.With("continuations", 0));
        var lines = page.Paint(120, 32, configuration, PaintLine.Blank);
        var text = string.Join('\n', lines.Select(line => line.Plain));
        Assert.Contains("Active / Waiting", text);
        Assert.Contains("task-one, task-two", text);
        Assert.Contains(Theme.Warning, lines[3].Markup);
        Assert.DoesNotContain("NaN", text);
        var failed = Check("failed") with { Verdict = "failed", Reason = "The check timed out.", Evidence = [] };
        page.Update(State(waiting, failed) with { Status = PursueStatus.Paused, Phase = PursuePhase.None });
        lines = page.Paint(120, 32, configuration, PaintLine.Blank);
        text = string.Join('\n', lines.Select(line => line.Plain));
        Assert.Contains("The check timed out.", text);
        Assert.Contains("No evidence was cited", text);
        Assert.Contains("R resume", lines[^1].Plain);
        page.Update(State(failed) with { Status = PursueStatus.Complete, Phase = PursuePhase.None });
        Assert.Contains("Objective complete", page.Paint(120, 32, configuration, PaintLine.Blank)[^2].Plain);
    }

    [Fact]
    public void Paint_RecolorsAnAlreadyOpenPageIncludingEvidenceAndBars()
    {
        var page = new PursuePage();
        page.Update(State(Check("first")));
        page.Paint(120, 32, Configuration(), PaintLine.Blank);
        Assert.True(Theme.TryApply(new Dictionary<string, string>
        {
            ["heading"] = "bold red", ["user"] = "green", ["accent"] = "yellow", ["rule"] = "blue",
            ["selected"] = "bold cyan"
        }, out _));
        try
        {
            var lines = page.Paint(120, 32, Configuration(), PaintLine.Blank);
            Assert.Contains("bold red", lines[0].Markup);
            Assert.Contains("yellow", lines[3].Markup);
            Assert.Contains("blue", lines[3].Markup);
            Assert.Contains(lines, line => line.Plain.Contains("Tests pass.") && line.Markup.Contains("green"));
            Assert.Contains(lines, line => line.Markup.Contains("bold cyan"));
        }
        finally
        {
            Theme.TryUseBuiltIn("default");
        }
    }

    [Fact]
    public void Update_HoldsSelectionAndScrollUntilLatestIsRequestedAndResetsForNewObjective()
    {
        var page = new PursuePage();
        var first = Check("first");
        var second = Check("second") with { Evidence = [new PursueEvidence(4, "tool_result", string.Join('\n', Enumerable.Repeat("evidence line", 100)))] };
        var state = State(first, second);
        page.Update(state);
        Assert.Equal(second.Id, page.SelectedId);
        page.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(second.Id, page.SelectedId);
        page.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(first.Id, page.SelectedId);
        page.Handle(Key(ConsoleKey.UpArrow));
        page.Paint(80, 24, Configuration(), PaintLine.Blank);
        page.Handle(Key(ConsoleKey.PageDown));
        Assert.True(page.Scroll > 0);
        var scrolled = page.Paint(80, 24, Configuration(), PaintLine.Blank);
        Assert.Contains(scrolled, line => line.Plain.Contains('┃'));
        var scroll = page.Scroll;
        var third = Check("third");
        page.Update(state with { History = [first, second, third] });
        Assert.Equal(second.Id, page.SelectedId);
        Assert.Equal(scroll, page.Scroll);
        page.Handle(Key(ConsoleKey.L));
        Assert.Equal(third.Id, page.SelectedId);
        Assert.True(page.FollowLatest);
        Assert.Equal(0, page.Scroll);
        page.Update(state with { Id = "replacement", History = [] });
        Assert.Null(page.SelectedId);
        Assert.Equal(0, page.Scroll);
    }

    [Fact]
    public void Update_RefreshesTheStopReasonBeforeTheFirstCheckAndFollowsNewChecks()
    {
        var page = new PursuePage();
        var state = State();
        page.Update(state);
        page.Paint(80, 24, Configuration(), PaintLine.Blank);
        page.Update(state with { Status = PursueStatus.Paused, Phase = PursuePhase.None, Reason = "Working model failed." });
        Assert.Contains("Working model failed.", string.Join('\n', page.Paint(80, 24, Configuration(), PaintLine.Blank).Select(line => line.Plain)));
        var first = Check("first");
        page.Update(state with { History = [first] });
        Assert.Equal(first.Id, page.SelectedId);
        var second = Check("second");
        page.Update(state with { History = [first, second] });
        Assert.Equal(second.Id, page.SelectedId);
    }

    [Fact]
    public void Paint_HandlesEmptyClearedLongAndUntrustedContentAndUnlimitedLimits()
    {
        var page = new PursuePage();
        var empty = string.Join('\n', page.Paint(80, 24, Configuration(), PaintLine.Blank).Select(line => line.Plain));
        Assert.Contains("No objective", empty);
        Assert.Contains("/pursue start", empty);
        var check = Check("first") with { Reason = "[red]literal[/]\u001b[2J", Evidence = [new(4, "user", "line\tvalue\n[bold]literal[/]\u001b[2J")] };
        page.Update(State(check) with { Objective = new string('界', 200) });
        var limits = new PursueConfiguration(PursueModelSettings.Default, PursueLimits.Default.With("continuations", null));
        var lines = page.Paint(80, 24, limits, PaintLine.Blank);
        Assert.All(lines, line => Assert.True(TextWidth.Measure(line.Plain) <= 80));
        Assert.Contains("unlimited", lines[3].Plain);
        Assert.Contains(lines, line => line.Plain.Contains("[bold]literal[/]"));
        Assert.All(lines, line => Assert.False(TerminalText.HasControls(line.Plain)));
        page.Update(null);
        Assert.Contains("No objective", page.Paint(80, 24, limits, PaintLine.Blank)[1].Plain);
    }

    [Fact]
    public async Task Renderer_OpensByShortcutOrCommandAndKeepsDraftAndLiveStream()
    {
        using var renderer = new SessionRenderer();
        renderer.SeedComposer("draft " + ImageMarkerText.Prefix + "[Image #7]");
        renderer.BeginTurn();
        renderer.OnStreamEvent(new ChatTextDelta(0, 0, ChatRole.Assistant, "before"));
        var progress = renderer.ChromeProgress;
        Assert.Null(await Dispatch(renderer, ConsoleKey.F9));
        Assert.True(renderer.PursuePageOpen);
        renderer.OnStreamEvent(new ChatTextDelta(0, 0, ChatRole.Assistant, "after"));
        Assert.Equal(progress, renderer.ChromeProgress);
        Assert.Null(await Dispatch(renderer, ConsoleKey.Enter));
        Assert.Null(await Dispatch(renderer, ConsoleKey.Escape));
        Assert.False(renderer.PursuePageOpen);
        Assert.Equal("draft " + ImageMarkerText.Prefix + "[Image #7]", await Dispatch(renderer, ConsoleKey.Enter));
        renderer.SeedComposer("/pursue view");
        Assert.Null(await Dispatch(renderer, ConsoleKey.Enter));
        Assert.True(renderer.PursuePageOpen);
        Assert.True(renderer.TryClearComposer());
        Assert.False(renderer.PursuePageOpen);
    }

    [Fact]
    public async Task Projection_UpdatesDuringChildInspectionAndRespectsModalPriority()
    {
        using var renderer = new SessionRenderer { PursueConfiguration = Configuration };
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new ChildSessionChanged(new ChildSessionDocument { Id = "child", Agent = "explore", Status = "running" }, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested("child"));
        projection.OnEvent(new PursueChanged(State(Check("restored"))));
        await Dispatch(renderer, ConsoleKey.F9);
        projection.OnEvent(new ActivityChanged(SessionActivity.CheckingObjective));
        Assert.Contains("Checking Objective", renderer.PursueFrame(80, 24)[^2].Plain);
        projection.OnEvent(new ChildEventReceived("child", Guid.NewGuid(),
            new ActivityChanged(SessionActivity.Compacting)));
        Assert.Contains("Checking Objective", renderer.PursueFrame(80, 24)[^2].Plain);
        projection.OnEvent(new RetryScheduled(new SessionRetryAttempt(2, "Retry", TimeSpan.FromSeconds(8))));
        Assert.Contains("Retrying In", renderer.PursueFrame(80, 24)[^2].Plain);
        projection.OnEvent(new ActivityChanged(SessionActivity.WaitingForTask));
        Assert.Contains("Waiting For Task", renderer.PursueFrame(80, 24)[^2].Plain);
        Assert.False(projection.ReturnToParent());
        renderer.PauseComposer();
        renderer.SetOverlay(["Approval"]);
        Assert.False(renderer.ClosePursuePage());
        await Dispatch(renderer, ConsoleKey.Escape);
        Assert.True(renderer.PursuePageOpen);
        renderer.ClearOverlay();
        renderer.ResumeComposer();
        Assert.Contains("Checks 1", string.Join('\n', renderer.PursueFrame(80, 24).Select(line => line.Plain)));
        await Dispatch(renderer, ConsoleKey.Escape);
        Assert.Equal("child", renderer.ChildFocus);
        Assert.True(projection.ReturnToParent());
        Assert.False(renderer.PursuePageOpen);
        await Dispatch(renderer, ConsoleKey.F9);
        projection.OnEvent(new ConversationCleared());
        Assert.False(renderer.PursuePageOpen);
    }

    [Fact]
    public async Task Projection_TurnCompletionKeepsThePageAndHistoryReplacementClosesIt()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new PursueChanged(State(Check("saved"))));
        await Dispatch(renderer, ConsoleKey.F9);
        projection.OnEvent(new PursueChanged(State(Check("saved")) with { Status = PursueStatus.Complete, Phase = PursuePhase.None }));
        Assert.True(renderer.PursuePageOpen);
        Assert.Contains("Complete", renderer.PursueFrame(80, 24)[1].Plain);
        projection.OnEvent(new HistoryReplayed([]));
        Assert.False(renderer.PursuePageOpen);
        await Dispatch(renderer, ConsoleKey.F9);
        Assert.Contains("No objective", renderer.PursueFrame(80, 24)[1].Plain);
    }

    [Fact]
    public async Task Renderer_PauseResumeAndErrorsUseTheSharedCommandPath()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new PursueChanged(State(Check("first"))));
        await Dispatch(renderer, ConsoleKey.F9);
        Assert.Equal("/pursue pause", await Dispatch(renderer, ConsoleKey.P));
        projection.OnEvent(new PursueChanged(State(Check("first")) with { Status = PursueStatus.Paused }));
        Assert.Equal("/pursue resume", await Dispatch(renderer, ConsoleKey.R));
        projection.OnEvent(new ErrorWritten("Finish the current turn before resuming."));
        Assert.Contains("Finish the current turn", renderer.PursueFrame(80, 24)[^2].Plain);
    }

    private static PursueConfiguration Configuration() => new(PursueModelSettings.Default, PursueLimits.Default);
    private static PursueState State(params PursueCheckRecord[] checks) => new("Fix tests", PursueStatus.Active,
        PursuePhase.Checking, Checks: checks.Length, Continuations: 2, Stalls: 1) { Id = "objective", History = checks };
    private static PursueCheckRecord Check(string id) => new(id, "objective", "run", 1, DateTimeOffset.UnixEpoch,
        "continue", "Tests need fixing.", "Run tests.", [new(4, "tool_result", "Tests pass.")], [], new TokenUsage(10, 5));
    private static InputKey Key(ConsoleKey key) => new(key, '\0', ConsoleModifiers.None);
    private static Task<string?> Dispatch(SessionRenderer renderer, ConsoleKey key) => renderer.DispatchBurstAsync(
        [new ConsoleKeyInfo(key == ConsoleKey.Enter ? '\r' : '\0', key, false, false, false)],
        static () => false, CancellationToken.None, checkSize: false);
}
