using Spectre.Console;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Display.Input;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;
using CrystalCode.Terminal;

namespace CrystalCode.Tests.Terminal;

public sealed class SessionRendererTests
{
    [Fact]
    public void SetChrome_UpdatesWorkspaceRoot()
    {
        var renderer = new SessionRenderer();
        renderer.WriteHeader("deepseek-v4-flash", "/old/workspace", planMode: false, ApprovalMode.Default);

        renderer.SetChrome(
            planMode: false,
            ApprovalMode.Default,
            workspaceRoot: "/new/workspace");

        Assert.Equal("/new/workspace", renderer.ChromeWorkspaceRoot);
    }

    [Fact]
    public void OnStreamEvent_EstimatesTokensWhenEnabled()
    {
        var renderer = new SessionRenderer { ShowEstimatedTokens = true };

        renderer.OnStreamEvent(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "abcdefgh"));

        Assert.Equal("~2 Tokens", renderer.ChromeTokenEstimate);
    }

    [Fact]
    public void OnStreamEvent_OmitsTokenEstimateWhenDisabled()
    {
        var renderer = new SessionRenderer();

        renderer.OnStreamEvent(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "abcdefgh"));

        Assert.Equal(string.Empty, renderer.ChromeTokenEstimate);
    }

    [Fact]
    public void ShowEstimatedTokens_ClearsEstimateWhenTurnedOff()
    {
        var renderer = new SessionRenderer { ShowEstimatedTokens = true };
        renderer.OnStreamEvent(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "abcdefgh"));

        renderer.ShowEstimatedTokens = false;

        Assert.Equal(string.Empty, renderer.ChromeTokenEstimate);
    }

    [Fact]
    public void TryClearComposer_ClearsTextThenReturnsFalse()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");

        Assert.True(renderer.TryClearComposer());
        Assert.False(renderer.TryClearComposer());
    }

    [Fact]
    public void TryClearComposer_IsFalseWhenEmpty()
    {
        var renderer = new SessionRenderer();

        Assert.False(renderer.TryClearComposer());
    }

    [Fact]
    public void TryClearComposer_NotifiesHostToDiscardDraftAttachments()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("draft [Image #1]");
        string? edited = null;
        renderer.OnComposerEdited = text => edited = text;

        Assert.True(renderer.TryClearComposer());

        Assert.Equal(string.Empty, edited);
    }

    [Fact]
    public void TryReadKeyScroll_PlainArrowReservedForQuestionSelection()
    {
        var up = new InputKey(ConsoleKey.UpArrow, '\0', ConsoleModifiers.None);

        Assert.False(SessionRenderer.TryReadKeyScroll(
            up,
            scrollPlainArrows: false,
            pageRows: 10,
            out _));
        Assert.True(SessionRenderer.TryReadKeyScroll(
            up,
            scrollPlainArrows: true,
            pageRows: 10,
            out _));
    }

    [Fact]
    public void OnRetry_SetsRetryCaptionAndKeepsLastUsage()
    {
        var renderer = new SessionRenderer { ContextWindow = 1000 };
        renderer.ShowLiveUsage(new TokenUsage(100, 20), null);

        renderer.OnRetry(new SessionRetryAttempt(2, "slow down", TimeSpan.FromSeconds(8)));

        Assert.StartsWith("Retrying In ", renderer.ChromeProgress, StringComparison.Ordinal);
        Assert.Contains("(Attempt 2)", renderer.ChromeProgress, StringComparison.Ordinal);
        Assert.Equal(100, renderer.LastUsage?.InputTokenCount);
        Assert.Equal(20, renderer.LastUsage?.OutputTokenCount);
    }

    [Fact]
    public void ShowLiveUsage_RefreshesContextAndCumulativeCounts()
    {
        var renderer = new SessionRenderer { ContextWindow = 1_000 };
        renderer.ShowUsage(
            new TokenUsage(100, 20),
            new TokenUsage(12_900, 800));
        renderer.BeginTurn();

        renderer.ShowLiveUsage(new TokenUsage(200, 50), new TokenUsage(13_200, 870));

        Assert.Equal("CTX 25%  ·  13.2k IN / 870 OUT", renderer.ChromeUsage);
        Assert.Equal("14.1k Total", renderer.ChromeUsageTotal);
    }

    [Fact]
    public void ShowLiveUsage_KeepsCumulativeCountsWhenTheyAreUnchanged()
    {
        var renderer = new SessionRenderer { ContextWindow = 1_000 };
        renderer.ShowUsage(
            new TokenUsage(100, 20),
            new TokenUsage(12_900, 800));

        renderer.ShowLiveUsage(new TokenUsage(200, 50), new TokenUsage(12_900, 800));

        Assert.Equal("CTX 25%  ·  12.9k IN / 800 OUT", renderer.ChromeUsage);
        Assert.Equal("13.7k Total", renderer.ChromeUsageTotal);
    }

    [Fact]
    public async Task PumpUntilAsync_ReturnsWhenWakeCompletes()
    {
        var renderer = new SessionRenderer();

        await renderer.PumpUntilAsync(
            Task.CompletedTask,
            onSubmit: null,
            planMode: false,
            togglePlan: static () => false,
            CancellationToken.None);
    }

    [Fact]
    public async Task DispatchBurstAsync_PastesImageBeforeSubmittingSameBurst()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("describe ");
        renderer.OnImagePasteAsync = static _ => Task.FromResult<string?>("[Image #7]");

        var submitted = await renderer.DispatchBurstAsync(
            [
                new ConsoleKeyInfo('\u0016', ConsoleKey.V, false, false, true),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            ],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Contains("[Image #7]", submitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchBurstAsync_KeepsTextAfterImageInSameBurst()
    {
        var renderer = new SessionRenderer();
        renderer.OnImagePasteAsync = static _ => Task.FromResult<string?>("[Image #7]");

        var submitted = await renderer.DispatchBurstAsync(
            [
                new ConsoleKeyInfo('\u0016', ConsoleKey.V, false, false, true),
                new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            ],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Contains("[Image #7]x", submitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchBurstAsync_WheelDoesNotRecallPromptHistory()
    {
        var renderer = new SessionRenderer();
        renderer.SeedPromptHistory(["previous prompt"]);

        var wheel = "\u001b[<64;12;8M".Select(
            ch => new ConsoleKeyInfo(ch, default, false, false, false)).ToArray();
        var wheelSubmission = await renderer.DispatchBurstAsync(
            wheel,
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Null(wheelSubmission);
        Assert.Equal(string.Empty, submitted);
    }

    [Fact]
    public async Task DispatchBurstAsync_UpArrowStillRecallsPromptHistory()
    {
        var renderer = new SessionRenderer();
        renderer.SeedPromptHistory(["previous prompt"]);

        await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Equal("previous prompt", submitted);
    }

    [Fact]
    public async Task DispatchBurstAsync_StatsPageIgnoresComposerInputUntilClosed()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");
        renderer.ShowStatsPage(new Markup("Stats"));
        Assert.True(renderer.OverlayVisible);

        var typed = await renderer.DispatchBurstAsync(
            [
                new ConsoleKeyInfo('h', ConsoleKey.H, false, false, false),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            ],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var closed = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Null(typed);
        Assert.Null(closed);
        Assert.False(renderer.OverlayVisible);
        Assert.Equal("draft", submitted);
    }

    [Fact]
    public async Task TryClearComposer_ClosesStatsPageAndKeepsDraft()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");
        renderer.ShowStatsPage(new Markup("Stats"));

        Assert.True(renderer.TryClearComposer());
        Assert.False(renderer.OverlayVisible);

        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Equal("draft", submitted);
    }

    [Fact]
    public async Task DispatchBurstAsync_SideQuestionIgnoresComposerUntilClosed()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");
        renderer.ShowSideQuestion(new SideQuestionSnapshot(
            true,
            [],
            true,
            "why",
            string.Empty,
            null,
            false));
        Assert.True(renderer.SideQuestionOpen);

        var quit = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var typed = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('h', ConsoleKey.H, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var dismissed = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Null(quit);
        Assert.Null(typed);
        Assert.Null(dismissed);
        Assert.False(renderer.SideQuestionOpen);
        Assert.Equal("draft", submitted);
    }

    [Fact]
    public async Task DispatchBurstAsync_XClearsSideQuestionWithoutSubmitting()
    {
        var renderer = new SessionRenderer();
        var cleared = false;
        renderer.OnSideCleared = () => cleared = true;
        renderer.ShowSideQuestion(new SideQuestionSnapshot(
            true,
            [new SideExchange("why", "Because.")],
            false,
            string.Empty,
            string.Empty,
            null,
            false));

        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Null(submitted);
        Assert.True(cleared);
        Assert.False(renderer.SideQuestionOpen);
    }

    [Fact]
    public async Task DispatchBurstAsync_CtrlCClosesSideQuestionWithoutSubmitting()
    {
        var renderer = new SessionRenderer();
        var cancelled = false;
        renderer.OnSideCancelled = () => cancelled = true;
        renderer.SeedComposer("draft");
        renderer.ShowSideQuestion(new SideQuestionSnapshot(
            true,
            [],
            true,
            "why",
            string.Empty,
            null,
            true));

        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        Assert.Null(submitted);
        Assert.True(cancelled);
        Assert.False(renderer.SideQuestionOpen);
        var draft = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);
        Assert.Equal("draft", draft);

        renderer.ShowSideQuestion(new SideQuestionSnapshot(
            false,
            [],
            false,
            "why",
            string.Empty,
            "Side question cancelled.",
            false));
        Assert.False(renderer.SideQuestionOpen);
    }

    [Fact]
    public void PauseComposer_RestoresAnOpenSideQuestion()
    {
        var renderer = new SessionRenderer();
        renderer.ShowSideQuestion(SampleSide());

        renderer.PauseComposer();
        Assert.False(renderer.SideQuestionOpen);

        renderer.ResumeComposer();
        Assert.True(renderer.SideQuestionOpen);
    }

    [Fact]
    public async Task PauseComposer_LeavesADismissedSideQuestionClosed()
    {
        var renderer = new SessionRenderer();
        renderer.ShowSideQuestion(SampleSide());
        await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)],
            static () => false,
            CancellationToken.None,
            checkSize: false);

        renderer.PauseComposer();
        renderer.ResumeComposer();

        Assert.False(renderer.SideQuestionOpen);
    }

    [Fact]
    public void PauseComposer_ShowsASideQuestionThatArrivesWhilePaused()
    {
        var renderer = new SessionRenderer();
        renderer.PauseComposer();
        renderer.ShowSideQuestion(SampleSide());
        Assert.False(renderer.SideQuestionOpen);

        renderer.ResumeComposer();

        Assert.True(renderer.SideQuestionOpen);
    }

    [Fact]
    public void PauseComposer_IgnoresASideUpdateThatDoesNotAnnounce()
    {
        var renderer = new SessionRenderer();
        renderer.PauseComposer();
        renderer.ShowSideQuestion(new SideQuestionSnapshot(
            false,
            [new SideExchange("why", "Because.")],
            false,
            string.Empty,
            string.Empty,
            null,
            false));

        renderer.ResumeComposer();

        Assert.False(renderer.SideQuestionOpen);
    }

    [Fact]
    public void PauseComposer_DoesNotReopenAfterTheSideQuestionIsCleared()
    {
        var renderer = new SessionRenderer();
        renderer.ShowSideQuestion(SampleSide());
        renderer.PauseComposer();
        renderer.ShowSideQuestion(SideQuestionSnapshot.Empty);

        renderer.ResumeComposer();

        Assert.False(renderer.SideQuestionOpen);
    }

    private static SideQuestionSnapshot SampleSide() =>
        new(
            true,
            [new SideExchange("why", "Because.")],
            false,
            string.Empty,
            string.Empty,
            null,
            false);
}
