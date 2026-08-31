using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Prompts;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Approvals;

public sealed class ReviewTranscriptTests
{
    [Fact]
    public void Render_KeepsFirstAndLatestUser_WhenStatusFollowUp()
    {
        var text = ReviewTranscript.Render(
        [
            new ChatMessage(ChatRole.System, "You are Crystal Code."),
            new ChatMessage(ChatRole.User, "Run the tests and fix failures."),
            new ChatMessage(ChatRole.Assistant, "I will start with the failing suite."),
            new ToolCall("1", BashTool.ToolName, """{"command":"dotnet test"}"""),
            new ToolResult("1", "Failed: 1", ToolResultStatus.Failure),
            new ChatMessage(ChatRole.User, "how's it going?")
        ]);

        Assert.Contains("[User]: Run the tests and fix failures.", text, StringComparison.Ordinal);
        Assert.Contains("[User]: how's it going?", text, StringComparison.Ordinal);
        Assert.Contains("[Assistant]: I will start with the failing suite.", text, StringComparison.Ordinal);
        Assert.Contains("dotnet test", text, StringComparison.Ordinal);
        Assert.DoesNotContain("You are Crystal Code.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewTranscript.OmittedNote, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesSummary_WhenOlderUserTurnsWereFolded()
    {
        var text = ReviewTranscript.Render(
        [
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nFix App.cs tests."),
            new ChatMessage(ChatRole.User, "continue")
        ]);

        Assert.Contains(CompactionPrompt.Marker, text, StringComparison.Ordinal);
        Assert.Contains("Fix App.cs tests.", text, StringComparison.Ordinal);
        Assert.Contains("[User]: continue", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_KeepsUserAnchors_WhenOldToolResultsExceedBudget()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "Run the tests and fix failures.")
        };
        for (var i = 0; i < 6; i++)
        {
            items.Add(new ToolResult(i.ToString(), new string('x', 8_000)));
        }

        items.Add(new ChatMessage(ChatRole.User, "how's it going?"));
        var text = ReviewTranscript.Render(items);

        Assert.Contains("[User]: Run the tests and fix failures.", text, StringComparison.Ordinal);
        Assert.Contains("[User]: how's it going?", text, StringComparison.Ordinal);
        Assert.Contains(ReviewTranscript.OmittedNote, text, StringComparison.Ordinal);
        Assert.Contains("[truncated]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HasAuthorization_IsFalseWhenOnlyTheLiveSystemPromptExists()
    {
        Assert.False(
            ReviewTranscript.HasAuthorization(
                [new ChatMessage(ChatRole.System, "You are Crystal Code.")]));
        Assert.Equal(
            string.Empty,
            ReviewTranscript.Render([new ChatMessage(ChatRole.System, "You are Crystal Code.")]));
    }

    [Fact]
    public void HasAuthorization_IsTrueWhenOnlyASummaryExists()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nFix App.cs.")
        };

        Assert.True(ReviewTranscript.HasAuthorization(items));
        Assert.Contains("Fix App.cs.", ReviewTranscript.Render(items), StringComparison.Ordinal);
    }
}
