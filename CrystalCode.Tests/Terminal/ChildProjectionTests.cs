using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ChildProjectionTests
{
    [Fact]
    public void ChildFocus_KeepsOtherChildUsageAndParentEventsOutOfViewAndRestoresParent()
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent-model", "/workspace", false, ApprovalMode.Default, "high", "", 1000)));
        projection.OnEvent(new UsageChanged(new TokenUsage(100, 10), new TokenUsage(100, 10), 1000, false));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Label = "first", Status = "running",
            Model = new AgentModelSettings { Model = "child-model", Context = 2000 },
            Usage = new SessionUsageDocument { InputTokenCount = 200, OutputTokenCount = 20 } };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.True(renderer.ChildViewActive);
        Assert.Equal(2000, renderer.ContextWindow);
        Assert.Equal(200, renderer.LastUsage!.InputTokenCount);
        projection.OnEvent(new ChildEventReceived("child-two", Guid.NewGuid(),
            new UsageChanged(new TokenUsage(900, 1), null, 4000, true)));
        projection.OnEvent(new UsageChanged(new TokenUsage(300, 30), new TokenUsage(1000, 100), 1000, true));
        Assert.Equal(2000, renderer.ContextWindow);
        Assert.Equal(200, renderer.LastUsage.InputTokenCount);
        projection.OnEvent(new ChildEventReceived(child.Id, Guid.NewGuid(),
            new UsageChanged(new TokenUsage(220, 22), null, 2000, true)));
        Assert.Equal(220, renderer.LastUsage.InputTokenCount);
        Assert.True(projection.ReturnToParent());
        Assert.False(renderer.ChildViewActive);
        Assert.Equal(1000, renderer.ContextWindow);
        Assert.Equal(300, renderer.LastUsage.InputTokenCount);
        Assert.Contains("1000 IN", renderer.ChromeUsage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "/child parent")]
    [InlineData("draft", null)]
    public async Task ChildEscape_ReturnsOnlyWhenComposerIsEmpty(string draft, string? expected)
    {
        var renderer = new SessionRenderer { ChildViewActive = true };
        if (draft.Length > 0)
        {
            renderer.SeedComposer(draft);
        }
        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false);
        Assert.Equal(expected, submitted);
        if (draft.Length > 0)
        {
            var text = await renderer.DispatchBurstAsync(
                [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
                static () => false, CancellationToken.None, checkSize: false);
            Assert.Equal(draft, text);
        }
    }
}
