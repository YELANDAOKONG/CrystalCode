using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class TranscriptCodecTests
{
    [Fact]
    public void WriteThenRead_RoundTripsMessagesAndTools()
    {
        var original = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "hi"),
            JsonToolCall("1", "read", """{"path":"a.txt"}"""),
            new ToolResult("1", "ok", ToolResultStatus.Success)
        };

        var restored = TranscriptCodec.Read(TranscriptCodec.Write(original));

        Assert.Equal(4, restored.Count);
        Assert.Equal("work", ((ChatMessage)restored[0]).Text);
        Assert.Equal("1", ((ToolCall)restored[2]).CallId);
        Assert.Equal(
            ToolInputKind.Json,
            ((ToolCall)restored[2]).InputKind);
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
            JsonToolCall("1", "read", "{}"),
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
    public void WriteThenRead_RoundTripsTextToolInputKind()
    {
        var original = new List<ChatItem>
        {
            new ChatMessage(ChatRole.User, "capture"),
            new ToolCall(
                "1",
                "capture",
                "first line\nsecond line",
                ToolInputKind.Text),
            new ToolResult("1", "captured")
        };

        var documents = TranscriptCodec.Write(original);
        Assert.Equal(
            ToolInputKind.Text.Value,
            documents[1].InputKind);

        var restored = TranscriptCodec.Read(documents);

        var call = Assert.IsType<ToolCall>(restored[1]);
        Assert.Equal(ToolInputKind.Text, call.InputKind);
        Assert.Equal("first line\nsecond line", call.Arguments);
    }

    [Fact]
    public void Read_DefaultsMissingInputKindToJson()
    {
        var documents = new List<SessionItemDocument>
        {
            new()
            {
                Kind = "message",
                Role = ChatRole.User.Value,
                Text = "read"
            },
            new()
            {
                Kind = "tool_call",
                CallId = "1",
                Name = "read",
                Arguments = "{}"
            },
            new()
            {
                Kind = "tool_result",
                CallId = "1",
                Text = "ok"
            }
        };

        var restored = TranscriptCodec.Read(documents);

        var call = Assert.IsType<ToolCall>(restored[1]);
        Assert.Equal(ToolInputKind.Json, call.InputKind);
    }

    [Fact]
    public void Read_RejectsUnknownInputKind()
    {
        var documents = new List<SessionItemDocument>
        {
            new()
            {
                Kind = "message",
                Role = ChatRole.User.Value,
                Text = "read"
            },
            new()
            {
                Kind = "tool_call",
                CallId = "1",
                Name = "read",
                Arguments = "{}",
                InputKind = "future"
            },
            new()
            {
                Kind = "tool_result",
                CallId = "1",
                Text = "ok"
            }
        };

        var restored = TranscriptCodec.Read(documents);

        Assert.Equal(2, restored.Count);
        Assert.DoesNotContain(restored, item => item is ToolCall);
    }

    [Fact]
    public void WriteThenRead_RoundTripsReasoningTextAndState()
    {
        var state = new OpaqueReasoningState(
            "gemini.part",
            System.Text.Encoding.UTF8.GetBytes("{\"thoughtSignature\":\"signed\"}"));
        var original = new List<ChatItem>
        {
            new ChatMessage(ChatRole.User, "read"),
            new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText("looked", ReasoningTextKind.Summary)],
                state))
        };

        var restored = TranscriptCodec.Read(TranscriptCodec.Write(original));

        var reasoning = Assert.IsType<ChatReasoningItem>(restored[1]);
        Assert.Equal("looked", Assert.Single(reasoning.Content.TextSegments).Text);
        Assert.Equal(ReasoningTextKind.Summary, reasoning.Content.TextSegments[0].Kind);
        Assert.Equal(state, reasoning.Content.State);
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
