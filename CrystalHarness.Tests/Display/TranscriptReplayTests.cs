using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Display;
using CrystalHarness.Prompts;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class TranscriptReplayTests
{
    [Fact]
    public void Lines_SkipsLiveSystemAndKeepsConversation()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work system"),
            new ChatMessage(ChatRole.User, "read the file"),
            new ChatMessage(ChatRole.Assistant, "I will read it."),
            new ToolCall("1", "read", """{"path":"a.txt"}"""),
            new ToolResult("1", "hello", ToolResultStatus.Success)
        };

        var lines = TranscriptReplay.Lines(items);

        Assert.Equal(4, lines.Count);
        Assert.Equal(TranscriptKind.User, lines[0].Kind);
        Assert.Equal("read the file", lines[0].Text);
        Assert.Equal(TranscriptKind.Assistant, lines[1].Kind);
        Assert.Equal("I will read it.", lines[1].Text);
        Assert.Equal(TranscriptKind.Tool, lines[2].Kind);
        Assert.Contains("a.txt", lines[2].Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptKind.Result, lines[3].Kind);
        Assert.Contains("hello", lines[3].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_KeepsEarlierContextSummary()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work system"),
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nRead a.txt."),
            new ChatMessage(ChatRole.User, "continue")
        };

        var lines = TranscriptReplay.Lines(items);

        Assert.Equal(2, lines.Count);
        Assert.Equal(TranscriptKind.Note, lines[0].Kind);
        Assert.Contains("Read a.txt.", lines[0].Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptKind.User, lines[1].Kind);
    }
}
