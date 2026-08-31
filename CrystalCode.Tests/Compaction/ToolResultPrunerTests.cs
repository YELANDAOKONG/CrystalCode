using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Compaction;

using Xunit;

namespace CrystalCode.Tests.Compaction;

public sealed class ToolResultPrunerTests
{
    [Fact]
    public void Prune_ClearsOldToolOutputOutsideTheProtectedBand()
    {
        var old = new string('x', 80);
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "first"),
            new ToolCall("1", "read", "{}"),
            new ToolResult("1", old),
            new ChatMessage(ChatRole.User, "second"),
            new ChatMessage(ChatRole.User, "third"),
            new ToolCall("2", "read", "{}"),
            new ToolResult("2", "recent")
        };

        var pruned = ToolResultPruner.Prune(
            items,
            ContextCompactor.OmittedResultText,
            protectTokens: 1,
            minimumPruneTokens: 1);

        Assert.Contains(
            pruned,
            item => item is ToolResult result
                && result.CallId == "1"
                && result.Text == ContextCompactor.OmittedResultText);
        Assert.Contains(
            pruned,
            item => item is ToolResult { CallId: "2", Text: "recent" });
    }
}
