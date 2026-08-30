using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Compaction;
using CrystalHarness.Prompts;

using Xunit;

namespace CrystalHarness.Tests.Compaction;

public sealed class CompactionSelectionTests
{
    [Fact]
    public void Choose_KeepsRecentUserTurnInTheTail()
    {
        var split = CompactionSelection.Choose(Sample(), tailBudget: 8);

        Assert.Contains(split.Tail, item => item is ChatMessage { Role.Value: "user", Text: "third" });
        Assert.Contains(split.Head, item => item is ChatMessage { Role.Value: "user", Text: "first" });
        Assert.DoesNotContain(split.Head, item => item is ChatMessage { Role.Value: "user", Text: "third" });
    }

    [Fact]
    public void Choose_ReadsPreviousSummaryAndOmitsItFromHead()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nPrior."),
            new ChatMessage(ChatRole.User, "first"),
            new ChatMessage(ChatRole.User, "second")
        };

        var split = CompactionSelection.Choose(items, tailBudget: 2);

        Assert.Equal("Prior.", split.PreviousSummary);
        Assert.DoesNotContain(split.Head, CompactionSelection.IsSummary);
        Assert.DoesNotContain(split.Tail, CompactionSelection.IsSummary);
    }

    private static List<ChatItem> Sample() =>
    [
        new ChatMessage(ChatRole.System, "work"),
        new ChatMessage(ChatRole.User, "first"),
        new ChatMessage(ChatRole.Assistant, "ok"),
        new ToolCall("1", "read", """{"path":"a.txt"}"""),
        new ToolResult("1", "file contents"),
        new ChatMessage(ChatRole.User, "second"),
        new ChatMessage(ChatRole.User, "third"),
        new ChatMessage(ChatRole.Assistant, "done")
    ];
}
