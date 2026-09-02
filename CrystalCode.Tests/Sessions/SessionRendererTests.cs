using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Approvals;
using CrystalCode.Display.Input;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

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
        renderer.OnUsageUpdated(new TokenUsage(100, 20));

        renderer.OnRetry(new SessionRetryAttempt(2, "slow down", TimeSpan.FromSeconds(8)));

        Assert.StartsWith("Retrying In ", renderer.ChromeProgress, StringComparison.Ordinal);
        Assert.Contains("(Attempt 2)", renderer.ChromeProgress, StringComparison.Ordinal);
        Assert.Equal(100, renderer.LastUsage?.InputTokenCount);
        Assert.Equal(20, renderer.LastUsage?.OutputTokenCount);
    }

    [Fact]
    public void OnUsageUpdated_RefreshesContextWithoutReplacingCumulativeCounts()
    {
        var renderer = new SessionRenderer { ContextWindow = 1_000 };
        renderer.ShowUsage(
            new TokenUsage(100, 20),
            new TokenUsage(12_900, 800));

        renderer.OnUsageUpdated(new TokenUsage(200, 50));

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
}
