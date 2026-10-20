using Crystal.Tools;

using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class TaskBatchExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AdjacentTasksOverlapButOrdinaryToolsAreBarriersAndResultsKeepOrder()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ordinary = new List<string>();
        var taskCount = 0;
        var inner = new BatchTestExecutor(async call =>
        {
            if (call.Name == "task")
            {
                if (Interlocked.Increment(ref taskCount) == 2)
                {
                    started.TrySetResult();
                }
                await release.Task;
            }
            else
            {
                ordinary.Add(call.Name);
            }
            return new ToolResult(call.CallId, call.Name);
        });
        var executor = new TaskBatchExecutor(inner);
        var calls = new[] { "read", "task", "task", "edit", "task", "bash" }
            .Select((name, index) => new ToolCall(index.ToString(), name, "{}", ToolInputKind.Json)).ToArray();
        var pending = executor.ExecuteAsync(calls);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["read"], ordinary);
        release.SetResult();
        var results = await pending;
        Assert.Equal(["read", "edit", "bash"], ordinary);
        Assert.Equal(calls.Select(call => call.CallId), results.Select(result => result.CallId));
    }

    private sealed class BatchTestExecutor(Func<ToolCall, Task<ToolResult>> execute) : IToolExecutor
    {
        public IReadOnlyList<ToolDefinition> Definitions => [];
        public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls,
            CancellationToken cancellationToken = default) => [await execute(Assert.Single(calls))];
    }
}
