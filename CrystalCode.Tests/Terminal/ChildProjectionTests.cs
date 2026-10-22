using System.Reflection;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Display.Cards;
using CrystalCode.Display.Transcript;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Terminal;
using CrystalCode.Terminal.Approvals;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ChildProjectionTests
{
    [Fact]
    public void ApprovalPromptNotifications_DoNotWriteIntoTheFocusedStreamOrProgress()
    {
        using var renderer = new SessionRenderer();
        var prompt = new ApprovalPrompt(renderer);
        renderer.BeginTurn();
        renderer.OnStreamEvent(new ChatTextDelta(0, 0, ChatRole.Assistant, "before"));
        var progress = renderer.ChromeProgress;
        var call = new ToolCall("other", "read", "{}", ToolInputKind.Json);
        var classification = new ToolClassification(Risk.Read, Authority.Workspace, "other approval");

        prompt.NotifyReviewing(call);
        prompt.NotifyPassed(call, classification, ApprovalPassReason.Policy);
        Assert.Equal(progress, renderer.ChromeProgress);
        renderer.OnStreamEvent(new ChatTextDelta(0, 0, ChatRole.Assistant, "after"));
        Assert.Contains("beforeafter", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.DoesNotContain("other approval", TranscriptText(renderer), StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalEvents_AreIsolatedAndSurviveSwitchingAndChildCompletion()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        var first = new ChildSessionDocument { Id = "child-one", Agent = "explore", Status = "running" };
        var second = new ChildSessionDocument { Id = "child-two", Agent = "explore", Status = "running" };
        projection.OnEvent(new ChildSessionChanged(first, Guid.NewGuid()));
        projection.OnEvent(new ChildSessionChanged(second, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(first.Id));
        projection.OnEvent(new ChildEventReceived(first.Id, Guid.NewGuid(), Passed("first approval")));
        var firstProgress = renderer.ChromeProgress;
        projection.OnEvent(new ChildEventReceived(second.Id, Guid.NewGuid(), Passed("second approval")));
        projection.OnEvent(new ChildEventReceived(second.Id, Guid.NewGuid(),
            new ApprovalReviewing(new ToolCall("review", "write", "{}", ToolInputKind.Json))));
        projection.OnEvent(Passed("parent approval"));
        Assert.Equal(firstProgress, renderer.ChromeProgress);
        Assert.Contains("first approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.DoesNotContain("second approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.DoesNotContain("parent approval", TranscriptText(renderer), StringComparison.Ordinal);

        projection.OnEvent(new ChildViewRequested(second.Id));
        Assert.Contains("second approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.DoesNotContain("first approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.Equal(ProgressText.Reviewing, renderer.ChromeProgress);
        projection.OnEvent(new ChildSessionChanged(second with { Status = "completed" }, Guid.NewGuid()));
        Assert.Contains("second approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.Empty(renderer.ChromeProgress);

        projection.ReturnToParent();
        Assert.Contains("parent approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.DoesNotContain("second approval", TranscriptText(renderer), StringComparison.Ordinal);
        projection.OnEvent(new ChildViewRequested(second.Id));
        Assert.Contains("second approval", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.Empty(renderer.ChromeProgress);
    }

    [Fact]
    public void TodosChanged_UpdatesOnlyTheFocusedChildAndReplaysLatestState()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "general", Status = "running" };
        projection.OnEvent(new TodosChanged([new TodoItem("p", "parent todo", TodoStatus.Pending)]));
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        projection.OnEvent(new ChildEventReceived(child.Id, Guid.NewGuid(),
            new TodosChanged([new TodoItem("a", "child todo", TodoStatus.InProgress)])));
        projection.OnEvent(new ChildEventReceived("other", Guid.NewGuid(),
            new TodosChanged([new TodoItem("b", "other todo", TodoStatus.Completed)])));
        Assert.Equal("child todo", Assert.Single(TodoItems(renderer)).Content);
        projection.ReturnToParent();
        Assert.Equal("parent todo", Assert.Single(TodoItems(renderer)).Content);
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.Equal("child todo", Assert.Single(TodoItems(renderer)).Content);
    }

    private static ApprovalPassed Passed(string summary) => new(
        new ToolCall(summary, "read", "{}", ToolInputKind.Json),
        new ToolClassification(Risk.Read, Authority.Workspace, summary), ApprovalPassReason.Policy);

    private static IReadOnlyList<TodoBarItem> TodoItems(SessionRenderer renderer) =>
        Assert.IsType<List<TodoBarItem>>(typeof(SessionRenderer)
            .GetField("_todoItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer));

    [Fact]
    public void ChildFocus_KeepsOtherChildUsageAndParentEventsOutOfViewAndRestoresParent()
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent-model", "/workspace", false, ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnToParent_RestoresMessagesAndHiddenStreamWithoutChangingDraft(bool resumed)
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        if (resumed)
        {
            projection.OnEvent(new UserMessageSent("discarded history"));
            projection.OnEvent(new HistoryReplayed([new ChatMessage(ChatRole.User, "parent question")]));
        }
        else
        {
            projection.OnEvent(new UserMessageSent("parent question"));
        }
        projection.OnEvent(new TurnStarted());
        projection.OnEvent(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "before switching")));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Label = "research" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        renderer.SeedComposer("keep this draft");
        projection.OnEvent(new ChildViewRequested(child.Id));
        projection.OnEvent(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, " after switching")));
        Assert.DoesNotContain("parent question", TranscriptText(renderer), StringComparison.Ordinal);

        for (var index = 0; index < 2; index++)
        {
            Assert.Equal("/child parent", await renderer.DispatchBurstAsync(
                [new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)],
                static () => false, CancellationToken.None, checkSize: false));
            projection.OnEvent(new ChildViewRequested(null));
            var text = TranscriptText(renderer);
            Assert.Contains("parent question", text, StringComparison.Ordinal);
            Assert.Contains("before switching after switching", text, StringComparison.Ordinal);
            Assert.DoesNotContain("discarded history", text, StringComparison.Ordinal);
            Assert.Equal(1, text.Split("parent question", StringSplitOptions.None).Length - 1);
            if (index == 0)
            {
                projection.OnEvent(new ChildViewRequested(child.Id));
            }
        }
        Assert.Equal("keep this draft", await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
    }

    [Fact]
    public async Task ReturnToParent_DoesNotRestoreRecalledTextIntoComposerAgain()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new UserMessageSent("recalled question"));
        projection.OnEvent(new UserMessageRecalled("recalled question"));
        renderer.TryClearComposer();
        renderer.SeedComposer("new draft");
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Label = "research" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.True(projection.ReturnToParent());
        Assert.DoesNotContain("recalled question", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.Equal("new draft", await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
    }

    [Fact]
    public void ReturnToParent_DoesNotResurrectClearedMessages()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new UserMessageSent("cleared question"));
        projection.OnEvent(new ConversationCleared());
        projection.OnEvent(new UserMessageSent("current question"));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Label = "research" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.True(projection.ReturnToParent());
        Assert.Contains("current question", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.DoesNotContain("cleared question", TranscriptText(renderer), StringComparison.Ordinal);
    }

    private static string TranscriptText(SessionRenderer renderer)
    {
        var field = typeof(SessionRenderer).GetField("_log", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var log = Assert.IsType<TranscriptLog>(field.GetValue(renderer));
        return string.Join("\n", log.BuildLines(120).Select(line => line.Plain));
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
