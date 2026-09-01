using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Approvals;
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
}
