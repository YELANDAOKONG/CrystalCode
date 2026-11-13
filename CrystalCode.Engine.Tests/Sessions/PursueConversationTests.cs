using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Sessions.Pursue;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PursueConversationTests
{
    [Fact]
    public void Render_CapturesTheShownEvidenceAfterSummaryAndTruncation()
    {
        ChatItem[] items =
        [
            new ChatMessage(ChatRole.System, "Private system prompt."),
            new ChatMessage(ChatRole.System, "## Earlier context\nOld summary."),
            new ChatMessage(ChatRole.User, "Fix the tests."),
            new ChatMessage(ChatRole.Assistant, new string('a', PursueConversation.EntryMaxCharacters + 1)),
            new ToolResult("call", "Tests pass.", ToolResultStatus.Success)
        ];
        var rendering = PursueConversation.Render(items, 100_000);
        Assert.Equal(new[] { 1, 2, 3 }, rendering.Evidence.Select(entry => entry.Number));
        Assert.DoesNotContain(rendering.Evidence, entry => entry.Text.Contains("Private system prompt."));
        var truncated = rendering.Evidence[1];
        Assert.EndsWith("\n[truncated]", truncated.Text);
        Assert.Contains("[2] " + truncated.Text, rendering.Text);
        var tail = PursueConversation.Render(items, 100);
        var evidence = Assert.Single(tail.Evidence);
        Assert.Equal(3, evidence.Number);
        Assert.Equal("tool_result", evidence.Kind);
        Assert.Contains("Tests pass.", evidence.Text);
        Assert.DoesNotContain("Old summary.", evidence.Text);
    }
}
