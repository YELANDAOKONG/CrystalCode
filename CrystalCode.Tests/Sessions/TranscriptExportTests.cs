using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Compaction;
using CrystalCode.Prompts;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class TranscriptExportTests
{
    [Fact]
    public void ConversationItems_SkipsLiveSystemButKeepsCompactionSummary()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "live system"),
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nPrior work."),
            new ChatMessage(ChatRole.User, "hello"),
            new ToolCall("1", "read", """{"path":"a.txt"}"""),
            new ToolResult("1", "contents")
        };

        var exported = TranscriptExport.ConversationItems(items);

        Assert.Equal(4, exported.Count);
        Assert.DoesNotContain(exported, item => item is ChatMessage { Text: "live system" });
        Assert.Contains(
            exported,
            item => item is ChatMessage message && CompactionSelection.IsSummary(message));
    }

    [Fact]
    public void RenderMarkdown_OmitsSystemByDefault()
    {
        var metadata = new SessionExportMetadata(
            "abc123",
            "/tmp/demo",
            "deepseek",
            "deepseek-v4-flash",
            "default",
            false,
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.User, "hello"),
            new ChatMessage(ChatRole.Assistant, "hi")
        };

        var markdown = TranscriptExport.RenderMarkdown(metadata, items, [], null);

        Assert.Contains("# Crystal Code session abc123", markdown, StringComparison.Ordinal);
        Assert.Contains("### User", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## System", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMarkdown_IncludesSystemWhenProvided()
    {
        var metadata = new SessionExportMetadata(
            "abc123",
            "/tmp/demo",
            "deepseek",
            "deepseek-v4-flash",
            "default",
            false,
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        var markdown = TranscriptExport.RenderMarkdown(
            metadata,
            [new ChatMessage(ChatRole.User, "hello")],
            [],
            "system body");

        Assert.Contains("## System", markdown, StringComparison.Ordinal);
        Assert.Contains("system body", markdown, StringComparison.Ordinal);
    }
}
