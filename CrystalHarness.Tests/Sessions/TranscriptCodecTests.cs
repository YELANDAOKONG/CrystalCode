using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Compaction;
using CrystalHarness.Prompts;
using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class TranscriptCodecTests
{
    [Fact]
    public void WriteThenRead_RoundTripsMessagesAndTools()
    {
        var original = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "hi"),
            new ToolCall("1", "read", """{"path":"a.txt"}"""),
            new ToolResult("1", "ok", ToolResultStatus.Success)
        };

        var restored = TranscriptCodec.Read(TranscriptCodec.Write(original));

        Assert.Equal(4, restored.Count);
        Assert.Equal("work", ((ChatMessage)restored[0]).Text);
        Assert.Equal("1", ((ToolCall)restored[2]).CallId);
        Assert.Equal("ok", ((ToolResult)restored[3]).Text);
    }

    [Fact]
    public void WriteThenRead_RoundTripsCompactedSummaryAndOmittedTools()
    {
        var summary = CompactionPrompt.Marker + "\n## Objective\n- Edit App.cs.";
        var original = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.System, summary),
            new ToolCall("1", "read", "{}"),
            new ToolResult("1", ContextCompactor.OmittedResultText),
            new ChatMessage(ChatRole.User, "continue")
        };

        var restored = TranscriptCodec.Read(TranscriptCodec.Write(original));

        Assert.Equal(5, restored.Count);
        Assert.True(CompactionSelection.IsSummary(restored[1]));
        Assert.Equal(summary, ((ChatMessage)restored[1]).Text);
        Assert.Equal(ContextCompactor.OmittedResultText, ((ToolResult)restored[3]).Text);
        Assert.Equal("continue", ((ChatMessage)restored[4]).Text);
        Assert.True(TranscriptCodec.HasConversation(restored));
    }

    [Fact]
    public void HasConversation_IsTrueForSummaryWithoutUserTail()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nPrior work.")
        };

        Assert.True(TranscriptCodec.HasConversation(items));
        Assert.False(
            TranscriptCodec.HasConversation([new ChatMessage(ChatRole.System, "work")]));
    }
}
