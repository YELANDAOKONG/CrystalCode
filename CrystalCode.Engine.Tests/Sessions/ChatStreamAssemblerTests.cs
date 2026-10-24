using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ChatStreamAssemblerTests
{
    [Fact]
    public void ToResponse_AssemblesAssistantText()
    {
        var assembler = new ChatStreamAssembler();
        assembler.Apply(new ChatTextDelta(0, 0, ChatRole.Assistant, "Hel"));
        assembler.Apply(new ChatTextDelta(0, 0, ChatRole.Assistant, "lo"));
        assembler.Apply(new ChatCandidateCompleted(0, FinishReason.Stop));
        assembler.Apply(new ChatUsageReceived(new TokenUsage(3, 2)));

        var response = assembler.ToResponse();

        var message = Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]);
        Assert.Equal("Hello", message.Text);
        Assert.Equal(3, response.Usage?.InputTokenCount);
        Assert.Equal(2, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public void ToResponse_KeepsLatestUsageSnapshot()
    {
        var assembler = new ChatStreamAssembler();
        assembler.Apply(new ChatUsageReceived(new TokenUsage(3, 1)));
        assembler.Apply(new ChatTextDelta(0, 0, ChatRole.Assistant, "Hi"));
        assembler.Apply(new ChatUsageReceived(new TokenUsage(3, 7)));
        assembler.Apply(new ChatCandidateCompleted(0, FinishReason.Stop));

        var response = assembler.ToResponse();

        Assert.Equal(new TokenUsage(3, 7), response.Usage);
    }

    [Fact]
    public void ToResponse_KeepsSummaryAndTraceOnOneReasoningItem()
    {
        var assembler = new ChatStreamAssembler();
        assembler.Apply(new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Summary, "sum"));
        assembler.Apply(new ChatReasoningTextDelta(
            0,
            0,
            1_000_000,
            ReasoningTextKind.Trace,
            "raw"));
        assembler.Apply(new ChatCandidateCompleted(0, FinishReason.Stop));

        var response = assembler.ToResponse();

        var reasoning = Assert.IsType<ChatReasoningItem>(response.Candidates[0].Items[0]);
        Assert.Equal(ReasoningTextKind.Summary, reasoning.Content.TextSegments[0].Kind);
        Assert.Equal("sum", reasoning.Content.TextSegments[0].Text);
        Assert.Equal(ReasoningTextKind.Trace, reasoning.Content.TextSegments[1].Kind);
        Assert.Equal("raw", reasoning.Content.TextSegments[1].Text);
    }

    [Fact]
    public void ToResponse_PreservesTextToolInput()
    {
        var assembler = new ChatStreamAssembler();
        assembler.Apply(new ChatToolCallDelta(
            0,
            0,
            "call",
            "capture",
            "first line\n",
            ToolInputKind.Text));
        assembler.Apply(new ChatToolCallDelta(
            0,
            0,
            "",
            "",
            "second line",
            ToolInputKind.Text));
        assembler.Apply(new ChatCandidateCompleted(0, FinishReason.ToolCalls));

        var response = assembler.ToResponse();

        var call = Assert.IsType<ToolCall>(response.Candidates[0].Items[0]);
        Assert.Equal(ToolInputKind.Text, call.InputKind);
        Assert.Equal("first line\nsecond line", call.Arguments);
    }

    [Fact]
    public void ToResponse_RejectsChangedToolInputKind()
    {
        var assembler = new ChatStreamAssembler();
        assembler.Apply(new ChatToolCallDelta(
            0,
            0,
            "call",
            "capture",
            "{}",
            ToolInputKind.Text));

        var failure = Assert.Throws<InvalidOperationException>(
            () => assembler.Apply(new ChatToolCallDelta(
                0,
                0,
                "",
                "",
                "{}",
                ToolInputKind.Json)));

        Assert.Contains("changed its input kind", failure.Message);
    }
}
