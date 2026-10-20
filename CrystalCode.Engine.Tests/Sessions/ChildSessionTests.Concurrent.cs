using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed partial class ChildSessionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ParallelTasks_UseSeparateIdsAndRespectSessionConcurrency(int concurrency)
    {
        var parentRounds = 0;
        var started = 0;
        var active = 0;
        var maximum = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RoutingStreamingClient(async (request, token) =>
        {
            if (request.Tools.Any(tool => tool.Name == "task"))
            {
                if (Interlocked.Increment(ref parentRounds) > 1)
                {
                    return Text("parent report");
                }
                return Enumerable.Range(0, 3).Select(index => (ChatStreamEvent)new ChatToolCallDelta(0, index,
                    "task" + index, "task", JsonSerializer.Serialize(new { agent = "explore", label = "part" + index, prompt = "part" + index }),
                    ToolInputKind.Json)).Concat([new ChatUsageReceived(new TokenUsage(20, 3, 1)),
                        new ChatCandidateCompleted(0, FinishReason.ToolCalls)]).ToArray();
            }
            var count = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = maximum;
            }
            while (count > observed && Interlocked.CompareExchange(ref maximum, count, observed) != observed);
            if (Interlocked.Increment(ref started) == concurrency)
            {
                ready.TrySetResult();
            }
            try
            {
                await release.Task.WaitAsync(token);
                var question = request.Items.OfType<ChatMessage>().Last(message => message.Role == ChatRole.User).Text;
                return Text("answer:" + question);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        using var session = new HeadlessSession(client, settings: Settings(concurrency));
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate three independent tasks", CancellationToken.None);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(concurrency, Volatile.Read(ref active));
        release.SetResult();
        await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await session.Session.FinishTurnAsync();
        var saved = session.ReadSaved();
        Assert.Equal(3, saved.Children.Count);
        Assert.Equal(3, saved.Children.Select(child => child.Id).Distinct().Count());
        Assert.Equal(concurrency, maximum);
        Assert.Equal(60, saved.CumulativeUsage!.InputTokenCount);
        foreach (var child in saved.Children)
        {
            Assert.Equal("completed", child.Status);
            Assert.Contains(child.Archive, item => item.Text == "answer:" + child.Label);
            Assert.Single(child.Archive, item => item.Role == "assistant");
            Assert.DoesNotContain(child.Tools, name => name == "task");
        }
        var calls = session.Observer.Events.OfType<ToolCallsIssued>().Single().Calls;
        var results = session.Observer.Events.OfType<ToolResultsReceived>().Single().Results;
        Assert.Equal(calls.Select(call => call.CallId), results.Select(result => result.CallId));
    }

    [Fact]
    public async Task Cancel_StopsRunningAndQueuedChildrenWithoutStartingQueuedModels()
    {
        var parent = false;
        var childStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childRequests = 0;
        var client = new RoutingStreamingClient(async (request, token) =>
        {
            if (request.Tools.Any(tool => tool.Name == "task"))
            {
                Assert.False(parent);
                parent = true;
                return new ChatStreamEvent[]
                {
                    new ChatToolCallDelta(0, 0, "one", "task", """{"agent":"explore","label":"one","prompt":"one"}""", ToolInputKind.Json),
                    new ChatToolCallDelta(0, 1, "two", "task", """{"agent":"explore","label":"two","prompt":"two"}""", ToolInputKind.Json),
                    new ChatUsageReceived(new TokenUsage(20, 3, 1)), new ChatCandidateCompleted(0, FinishReason.ToolCalls)
                };
            }
            Interlocked.Increment(ref childRequests);
            childStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Text("unreachable");
        });
        using var session = new HeadlessSession(client, settings: Settings(1));
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await childStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.Session.TryInterrupt());
        await session.Session.TurnTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await session.Session.FinishTurnAsync();
        Assert.Equal(1, childRequests);
        Assert.All(session.ReadSaved().Children, child => Assert.Equal("interrupted", child.Status));
    }

    [Fact]
    public async Task ResumeUnderPlan_RemovesSavedWriteTools()
    {
        using var first = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"general","label":"first","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: Settings());
        await first.Session.StartAsync(CancellationToken.None);
        await first.RunTurnAsync("delegate");
        var document = first.ReadSaved();
        var child = Assert.Single(document.Children);
        Assert.Contains("write", child.Tools);
        document.PlanMode = true;
        var args = JsonSerializer.Serialize(new { agent = "general", label = "continue", prompt = "write", sessionId = child.Id });
        var model = new ScriptedStreamingClient(Tool("task", args), Tool("write", """{"path":"bad.txt","contents":"bad"}"""),
            Text("blocked"), Text("parent"));
        using var resumed = new HeadlessSession(model, document, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.False(File.Exists(Path.Combine(resumed.WorkspacePath, "bad.txt")));
        Assert.Equal(child.Id, Assert.Single(resumed.ReadSaved().Children).Id);
    }

    [Fact]
    public async Task ChildCheckpoint_PersistsLiveParentToolResultsBeforeParentFinishes()
    {
        var rounds = 0;
        var parentWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RoutingStreamingClient(async (request, token) =>
        {
            if (!request.Tools.Any(tool => tool.Name == "task"))
            {
                return Text("child completed");
            }
            if (Interlocked.Increment(ref rounds) == 1)
            {
                return Tool("task", """{"agent":"explore","label":"checkpoint","prompt":"inspect"}""");
            }
            parentWaiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Text("unreachable");
        });
        using var session = new HeadlessSession(client, settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.Session.SubmitAsync("delegate", CancellationToken.None);
        await parentWaiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var checkpoint = session.ReadSaved();
        var child = Assert.Single(checkpoint.Children);
        Assert.Equal(30, checkpoint.CumulativeUsage!.InputTokenCount);
        Assert.Equal(2, checkpoint.ModelCalls);
        var savedCall = Assert.Single(checkpoint.Items, item => item.Kind == "tool_call");
        var savedResult = Assert.Single(checkpoint.Items, item => item.Kind == "tool_result");
        Assert.Equal(savedCall.CallId, savedResult.CallId);
        Assert.Contains(child.Id, savedResult.Text, StringComparison.Ordinal);
        Assert.Contains("child completed", savedResult.Text, StringComparison.Ordinal);
        session.Session.TryInterrupt();
        await session.Session.FinishTurnAsync();
        using var resumed = new HeadlessSession(new ScriptedStreamingClient(Text("resumed parent")), checkpoint, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.Equal(40, resumed.ReadSaved().CumulativeUsage!.InputTokenCount);
        Assert.Equal(child.Id, Assert.Single(resumed.ReadSaved().Children).Id);
    }

    [Fact]
    public async Task Fork_CopiesChildrenWithoutSharingMutableTranscriptOrCountingUsageAgain()
    {
        using var session = new HeadlessSession(new ScriptedStreamingClient(
            Tool("task", """{"agent":"explore","label":"fork","prompt":"inspect"}"""), Text("child"), Text("parent")), settings: Settings());
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var source = session.ReadSaved();
        var fork = SessionFork.Create(source, "forked", session.WorkspacePath, DateTimeOffset.UtcNow);
        var original = Assert.Single(source.Children);
        var branched = Assert.Single(fork.Children);
        Assert.Equal(original.Id, branched.Id);
        Assert.Equal(source.CumulativeUsage!.InputTokenCount, fork.CumulativeUsage!.InputTokenCount);
        branched.Archive[0].Text = "changed branch";
        Assert.NotEqual("changed branch", original.Archive[0].Text);
        using var resumed = new HeadlessSession(new ScriptedStreamingClient(Text("next")), fork, settings: Settings());
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.Equal(50, resumed.ReadSaved().CumulativeUsage!.InputTokenCount);
    }

    [Fact]
    public async Task Task_RetriesChildUsingSessionRetryAndKeepsCallCountLogical()
    {
        var parents = 0;
        var children = 0;
        var client = new RoutingStreamingClient((request, _) =>
        {
            if (request.Tools.Any(tool => tool.Name == "task"))
            {
                return Task.FromResult(Interlocked.Increment(ref parents) == 1
                    ? Tool("task", """{"agent":"explore","label":"retry","prompt":"inspect"}""") : Text("parent"));
            }
            if (Interlocked.Increment(ref children) == 1)
            {
                throw new HttpRequestException("503 service unavailable");
            }
            return Task.FromResult(Text("child"));
        });
        using var session = new HeadlessSession(client, settings: Settings().WithRetry(new RetrySettings(initialDelaySeconds: 0)));
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("delegate");
        var child = Assert.Single(session.ReadSaved().Children);
        Assert.Equal(2, children);
        Assert.Equal(1, child.ModelCalls);
        Assert.Contains(session.Observer.Events.OfType<ChildEventReceived>(), received => received.ChildSessionId == child.Id && received.Event is RetryScheduled);
        Assert.Equal(40, session.ReadSaved().CumulativeUsage!.InputTokenCount);
    }
}
