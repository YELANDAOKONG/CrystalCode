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

    [Fact]
    public async Task ReturnToParent_DoesNotReopenDismissedStatsPage()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new StatsReported(new SessionStatsReport(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 30,
            null, null, []), false));
        Assert.True(renderer.OverlayVisible);
        await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false);
        Assert.False(renderer.OverlayVisible);
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));

        Assert.True(projection.ReturnToParent());
        Assert.False(renderer.OverlayVisible);
    }

    [Fact]
    public void ReturnToParent_DoesNotReannounceDismissedSideQuestion()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new SideQuestionSnapshot(true, [new SideExchange("why", "because")],
            false, "", "", null, false));
        Assert.True(renderer.SideQuestionOpen);
        renderer.DismissSideQuestion();
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));

        Assert.True(projection.ReturnToParent());
        Assert.False(renderer.SideQuestionOpen);
    }

    [Fact]
    public void LiveChildEvents_RefreshPickerSnapshotsWithoutChangingFocusOrCommittingStream()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Status = "running" };
        var invocation = Guid.NewGuid();
        projection.OnEvent(new ChildSessionChanged(child, invocation));
        projection.OnEvent(new ChildViewRequested(child.Id));
        projection.OnEvent(new ChildEventReceived(child.Id, invocation,
            new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "before"))));
        projection.OnEvent(new ChildEventReceived(child.Id, invocation,
            new UsageChanged(new TokenUsage(220, 22), new TokenUsage(440, 44), 2000, true)));
        projection.OnEvent(new ChildEventReceived(child.Id, invocation,
            new TodosChanged([new TodoItem("a", "live todo", TodoStatus.InProgress)])));
        projection.OnEvent(new ChildEventReceived(child.Id, invocation,
            new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "after"))));
        projection.OnEvent(new ChildSessionChanged(child, Guid.Empty));

        var snapshot = Assert.Single(renderer.ChildSessions);
        Assert.Equal(220, snapshot.Usage!.InputTokenCount);
        Assert.Equal(440, snapshot.CumulativeUsage!.InputTokenCount);
        Assert.Equal("live todo", Assert.Single(snapshot.Todos).Content);
        Assert.Equal(child.Id, renderer.ChildFocus);
        Assert.Contains("beforeafter", TranscriptText(renderer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnToParent_DoesNotReseedOldPromptHistory()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new PromptHistoryLoaded(["old prompt"]));
        renderer.SeedComposer("recent prompt");
        Assert.Equal("recent prompt", await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.True(projection.ReturnToParent());

        Assert.Equal("recent prompt", await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
    }

    [Fact]
    public async Task ReturnToParent_DoesNotInvalidateImagesPastedAfterHistoryWasCleared()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        projection.OnEvent(new ImageHistoryInvalidated());
        renderer.OnImagePasteAsync = static _ => Task.FromResult<string?>("[Image #7]");
        await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\u0016', ConsoleKey.V, false, false, true)],
            static () => false, CancellationToken.None, checkSize: false);
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore" };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        projection.OnEvent(new ChildViewRequested(child.Id));
        Assert.True(projection.ReturnToParent());

        var submitted = await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false);
        Assert.Contains(ImageMarkerText.Prefix + "[Image #7]", submitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnToParent_LeavesFocusAndLiveStreamUntouchedWhileAModalOwnsInput()
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        var child = new ChildSessionDocument { Id = "child-one", Agent = "explore", Status = "running" };
        var invocation = Guid.NewGuid();
        projection.OnEvent(new ChildSessionChanged(child, invocation));
        projection.OnEvent(new ChildViewRequested(child.Id));
        projection.OnEvent(new ChildEventReceived(child.Id, invocation,
            new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "before"))));
        renderer.SetOverlay(["Approval modal"]);
        Assert.False(projection.ReturnToParent());
        Assert.Equal(child.Id, renderer.ChildFocus);
        Assert.True(renderer.OverlayVisible);
        projection.OnEvent(new ChildEventReceived(child.Id, invocation,
            new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "after"))));
        Assert.Contains("beforeafter", TranscriptText(renderer), StringComparison.Ordinal);
        renderer.ClearOverlay();
        Assert.True(projection.ReturnToParent());
    }

    [Theory]
    [InlineData(80)]
    [InlineData(120)]
    public void ChildChrome_ShowsIdentityAndNavigationWithoutExposingParentDraft(int width)
    {
        using var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new SessionStarted(new SessionChrome("parent", "/workspace", false,
            ApprovalMode.Default, "high", "", 1000, AgentsEnabled: true)));
        var child = new ChildSessionDocument
        {
            Id = "12345678-child", Agent = "explore", Label = "[research] " + new string('x', 120),
            Model = new AgentModelSettings { Model = "child-model" }
        };
        projection.OnEvent(new ChildSessionChanged(child, Guid.NewGuid()));
        renderer.SeedComposer("private parent draft");
        projection.OnEvent(new ChildViewRequested(child.Id));

        var line = Assert.Single(renderer.ProjectComposer(width, 1).Lines);
        Assert.Contains("Child 12345678", line.Plain, StringComparison.Ordinal);
        Assert.Contains("explore", line.Plain, StringComparison.Ordinal);
        Assert.Contains("Esc: parent", line.Plain, StringComparison.Ordinal);
        Assert.Contains("F7: children", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("private parent draft", line.Plain, StringComparison.Ordinal);
        Assert.True(CrystalCode.Display.Paint.TextWidth.Measure(line.Plain) <= width);
        Assert.DoesNotContain("Esc: parent", TranscriptText(renderer), StringComparison.Ordinal);
        Assert.Equal("child-model", renderer.ChromeModel);
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
        var navigation = Assert.Single(renderer.ProjectComposer(80, 8).Lines).Plain;
        Assert.Contains("Esc: parent", navigation, StringComparison.Ordinal);
        Assert.Contains("F7: children", navigation, StringComparison.Ordinal);
        if (draft.Length > 0)
        {
            Assert.DoesNotContain(draft, navigation, StringComparison.Ordinal);
        }
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
