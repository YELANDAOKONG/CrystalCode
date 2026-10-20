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

    [Fact]
    public void AgentsDisabled_ClearsChildFocusAndShortcutAvailability()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Label = "research" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.True(renderer.ChildViewActive);
        Assert.True(renderer.ChildSessionsEnabled);
        projection.OnEvent(new ChromeChanged(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: false)));
        Assert.False(renderer.ChildViewActive);
        Assert.False(renderer.ChildSessionsEnabled);
        Assert.Empty(renderer.ChildSessions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("draft")]
    [InlineData("/model")]
    public async Task ChildView_BlocksSubmissionAndEditingAndEscapePreservesParentDraft(string draft)
    {
        using var renderer = new SessionRenderer { ChildViewActive = true };
        renderer.SeedComposer(draft);
        var toggled = false;
        foreach (var key in new[]
        {
            new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
            new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false),
            new ConsoleKeyInfo('\u0016', ConsoleKey.V, false, false, true)
        })
        {
            Assert.Null(await renderer.DispatchBurstAsync([key], () => toggled = true,
                CancellationToken.None, checkSize: false));
        }
        Assert.False(toggled);
        Assert.All(renderer.ProjectComposer(80, 8).Lines, line => Assert.True(string.IsNullOrWhiteSpace(line.Plain)));
        Assert.Equal("/child parent", await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
        renderer.ChildViewActive = false;
        renderer.SetSlashCommands([]);
        Assert.Equal(draft, await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildShortcut_OpensFromEitherViewAndRespectsModalOwnership(bool childView)
    {
        using var renderer = new SessionRenderer { ChildViewActive = childView };
        renderer.SeedComposer("keep this draft");
        var key = new ConsoleKeyInfo('\0', ConsoleKey.F7, false, false, false);
        Assert.Equal("/child", await renderer.DispatchBurstAsync([key], static () => false,
            CancellationToken.None, checkSize: false));
        renderer.ChildSessionsEnabled = false;
        Assert.Null(await renderer.DispatchBurstAsync([key], static () => false,
            CancellationToken.None, checkSize: false));
        renderer.PauseComposer();
    }
}
