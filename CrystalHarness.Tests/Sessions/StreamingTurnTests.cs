using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class StreamingTurnTests
{
    [Fact]
    public async Task RunAsync_CompletesWhenModelReturnsText()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        var turn = CreateTurn(client);

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(1, result.ModelCallCount);
        Assert.Equal(0, result.ToolCallCount);
        Assert.Equal("done", Assert.IsType<ChatMessage>(result.Transcript[^1]).Text);
    }

    [Fact]
    public async Task RunAsync_ExecutesToolBatchThenCompletes()
    {
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "echo", "{}"),
            TextRound("ok"));
        var turn = CreateTurn(client);

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "echo")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(2, result.ModelCallCount);
        Assert.Equal(1, result.ToolCallCount);
        var toolResult = result.Transcript.OfType<ToolResult>().Single();
        Assert.Equal("c1", toolResult.CallId);
        Assert.Equal("echoed:{}", toolResult.Text);
        Assert.Equal("ok", Assert.IsType<ChatMessage>(result.Transcript[^1]).Text);
    }

    [Fact]
    public async Task RunAsync_StopsAtModelCallLimit()
    {
        var client = new ScriptedStreamingClient(ToolRound("c1", "echo", "{}"));
        var turn = CreateTurn(
            client,
            new TurnLimits(1, 8, TimeSpan.FromSeconds(5)));

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "loop")]);

        Assert.Equal(TurnStopReason.ModelCallLimitReached, result.StopReason);
        Assert.Equal(1, result.ModelCallCount);
        Assert.Equal(1, result.ToolCallCount);
    }

    private static StreamingTurn CreateTurn(
        IStreamingChatClient client,
        TurnLimits? limits = null) =>
        new(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            limits ?? new TurnLimits(8, 8, TimeSpan.FromSeconds(5)));

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        new ChatToolCallDelta(0, 0, callId, name, arguments),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];
}
