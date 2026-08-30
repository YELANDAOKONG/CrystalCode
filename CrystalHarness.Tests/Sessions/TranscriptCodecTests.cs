using Crystal.Chat;
using Crystal.Tools;

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
}
