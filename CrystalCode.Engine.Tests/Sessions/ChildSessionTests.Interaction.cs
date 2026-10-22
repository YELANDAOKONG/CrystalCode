using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed partial class ChildSessionTests
{
    [Fact]
    public async Task ChildApproval_EmitsSourceScopedEventsAndKeepsPromptNotifications()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"read","prompt":"inspect"}"""),
            Tool("read", """{"path":"notes.txt"}"""), Text("child"), Text("parent")), settings: Settings());
        await File.WriteAllTextAsync(Path.Combine(session.WorkspacePath, "notes.txt"), "notes");
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");

        var child = Assert.Single(session.ReadSaved().Children);
        var approval = Assert.Single(session.Observer.Events.OfType<ChildEventReceived>(),
            item => item.Event is ApprovalPassed);
        Assert.Equal(child.Id, approval.ChildSessionId);
        var passed = Assert.IsType<ApprovalPassed>(approval.Event);
        Assert.Equal("read", passed.Call.Name);
        Assert.Contains(child.Id, passed.Classification.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(session.Observer.Events.OfType<ApprovalPassed>(), item => item.Call.Name == "read");
        Assert.True(session.Approvals.PassCount > 0);
    }

    [Fact]
    public async Task RunningChild_PublishesTodosAndSupportsAwaitedPumpNavigationWithoutInterrupting()
    {
        var model = new HeldStreamingClient(
            Tool("task", """{"agent":"general","label":"plan","prompt":"make a plan"}"""),
            Tool("todowrite", """{"todos":[{"id":"a","content":"child todo","status":"in_progress"}]}"""));
        ChildSessionDocument? picked = null;
        using var session = new HeadlessSession(model, settings: Settings(), chooseChild: (children, _) =>
        {
            picked = Assert.Single(children);
            return Task.FromResult<string?>(picked.Id);
        });
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await model.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var changed = Assert.Single(session.Observer.Events.OfType<ChildEventReceived>(),
                item => item.Event is TodosChanged);
            var todo = Assert.Single(Assert.IsType<TodosChanged>(changed.Event).Items);
            Assert.Equal("child todo", todo.Content);
            Assert.Equal(TodoStatus.InProgress, todo.Status);
            Assert.DoesNotContain(session.Observer.Events.OfType<TodosChanged>(),
                item => item.Items.Any(value => value.Content == "child todo"));

            await session.Session.EnqueueAsync("/child", CancellationToken.None);
            Assert.NotNull(picked);
            Assert.Equal("running", picked.Status);
            Assert.Equal("child todo", Assert.Single(picked.Todos).Content);
            Assert.Equal(changed.ChildSessionId, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
            await session.Session.EnqueueAsync("/child " + picked.Id[..8], CancellationToken.None);
            Assert.Equal(picked.Id, session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
            await session.Session.EnqueueAsync("/child parent", CancellationToken.None);
            Assert.Null(session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
            await session.Session.EnqueueAsync("/agents", CancellationToken.None);
            Assert.DoesNotContain(session.Observer.Events.OfType<ErrorWritten>(),
                error => error.Text.Contains("Finish the current turn", StringComparison.Ordinal));
            Assert.True(session.Session.TurnActive);
            Assert.False(session.Session.TurnTask!.IsCompleted);
        }
        finally
        {
            session.Session.TryInterrupt();
            await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
            await session.Session.FinishTurnAsync();
        }
        var saved = Assert.Single(session.ReadSaved().Children);
        Assert.Equal("child todo", Assert.Single(saved.Todos).Content);
    }

    [Fact]
    public async Task PumpNavigation_AwaitsPickerAndPropagatesCancellation()
    {
        using var first = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"read","prompt":"inspect"}"""),
            Text("child"), Text("parent")), settings: Settings());
        await first.Session.StartAsync(CancellationToken.None);
        await first.RunTurnAsync("delegate");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new HeadlessSession(new ScriptedStreamingClient(Text("unused")),
            resume: first.ReadSaved(), settings: Settings(), chooseChild: async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return null;
            });
        await session.Session.StartAsync(CancellationToken.None);
        using var source = new CancellationTokenSource();
        var navigation = session.Session.EnqueueAsync("/child", source.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(navigation.IsCompleted);
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
        await session.Session.EnqueueAsync("/child parent", CancellationToken.None);
        Assert.Null(session.Observer.Events.OfType<ChildViewRequested>().Last().ChildSessionId);
    }
}
