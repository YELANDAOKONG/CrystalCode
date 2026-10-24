using Crystal.Tools;

using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CallScreeningExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UnknownToolFailsAloneAndTheRestStillRun()
    {
        var inner = new RecordingExecutor(JsonTool("read"), JsonTool("grep"));
        var executor = new CallScreeningExecutor(inner);

        var results = await executor.ExecuteAsync(
        [
            JsonToolCall("1", "read", "{}"),
            JsonToolCall("2", "find_placeholder", ""),
            JsonToolCall("3", "grep", "{}")
        ]);

        Assert.Equal(["1", "2", "3"], results.Select(result => result.CallId));
        Assert.Equal(ToolResultStatus.Success, results[0].Status);
        Assert.Equal(ToolResultStatus.Failure, results[1].Status);
        Assert.Equal(ToolResultStatus.Success, results[2].Status);
        Assert.Equal(["1", "3"], inner.SeenCallIds);
    }

    [Fact]
    public async Task ExecuteAsync_NamesTheAvailableToolsInSortedOrder()
    {
        var executor = new CallScreeningExecutor(new RecordingExecutor(JsonTool("zeta"), JsonTool("alpha")));

        var results = await executor.ExecuteAsync([JsonToolCall("1", "nope", "{}")]);

        Assert.Equal(
            "Tool 'nope' is not available. Available tools: alpha, zeta.",
            results[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_TextCallToAJsonToolFailsWithAnInputKindMessage()
    {
        var inner = new RecordingExecutor(JsonTool("read"));
        var executor = new CallScreeningExecutor(inner);

        var results = await executor.ExecuteAsync([new ToolCall("1", "read", "raw text", ToolInputKind.Text)]);

        Assert.Equal(ToolResultStatus.Failure, results[0].Status);
        Assert.Equal("Tool 'read' takes JSON arguments, not plain text input.", results[0].Text);
        Assert.Empty(inner.SeenCallIds);
    }

    [Fact]
    public async Task ExecuteAsync_JsonCallToATextToolFailsWithAnInputKindMessage()
    {
        var inner = new RecordingExecutor(ToolDefinition.Text("patch", "Applies a patch"));
        var executor = new CallScreeningExecutor(inner);

        var results = await executor.ExecuteAsync([JsonToolCall("1", "patch", "{}")]);

        Assert.Equal("Tool 'patch' takes plain text input, not JSON arguments.", results[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_AllRefusedNeverCallsTheInnerExecutor()
    {
        var inner = new RecordingExecutor(JsonTool("read"));
        var executor = new CallScreeningExecutor(inner);

        var results = await executor.ExecuteAsync(
            [JsonToolCall("1", "a", "{}"), JsonToolCall("2", "b", "{}")]);

        Assert.All(results, result => Assert.Equal(ToolResultStatus.Failure, result.Status));
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsResultOrderWhenTheBadCallComesFirst()
    {
        var executor = new CallScreeningExecutor(new RecordingExecutor(JsonTool("read")));

        var results = await executor.ExecuteAsync(
            [JsonToolCall("1", "bad", "{}"), JsonToolCall("2", "read", "{}")]);

        Assert.Equal(["1", "2"], results.Select(result => result.CallId));
        Assert.Equal(ToolResultStatus.Failure, results[0].Status);
        Assert.Equal(ToolResultStatus.Success, results[1].Status);
    }

    private static ToolDefinition JsonTool(string name) =>
        ToolDefinition.Json(name, System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement);

    private sealed class RecordingExecutor(params ToolDefinition[] definitions) : IToolExecutor
    {
        public IReadOnlyList<ToolDefinition> Definitions { get; } = definitions;

        public List<string> SeenCallIds { get; } = [];

        public int Calls { get; private set; }

        public Task<IReadOnlyList<ToolResult>> ExecuteAsync(
            IEnumerable<ToolCall> calls,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var results = new List<ToolResult>();
            foreach (var call in calls)
            {
                SeenCallIds.Add(call.CallId);
                results.Add(new ToolResult(call.CallId, "ok"));
            }

            return Task.FromResult<IReadOnlyList<ToolResult>>(results);
        }
    }
}
