using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Approvals;

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

    [Fact]
    public void Render_LabelsQuestionToolAnswersAsUserAnswers()
    {
        var text = ReviewTranscript.Render(
        [
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "Fix the failing tests."),
            new ToolCall("1", QuestionTool.ToolName, """{"questions":[]}"""),
            new ToolResult(
                "1",
                QuestionTool.AnswersPrefix + """{"answers":[["Yes, delete the file"]]}""",
                ToolResultStatus.Success)
        ]);

        Assert.Contains("[User answer]: ", text, StringComparison.Ordinal);
        Assert.Contains("Yes, delete the file", text, StringComparison.Ordinal);
        Assert.DoesNotContain(QuestionTool.AnswersPrefix, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_KeepsDismissedQuestionAsToolError()
    {
        var text = ReviewTranscript.Render(
        [
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "Fix the failing tests."),
            new ToolCall("1", QuestionTool.ToolName, """{"questions":[]}"""),
            new ToolResult("1", "The user dismissed the question.", ToolResultStatus.Failure)
        ]);

        Assert.Contains("[Tool error]: The user dismissed the question.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[User answer]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_KeepsAllUserTurns_WhenWithinMessageBudget()
    {
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.User, "Run the tests and fix failures."),
            new ChatMessage(ChatRole.User, "Middle one. " + new string('a', 7_888)),
            new ChatMessage(ChatRole.User, "Middle two. " + new string('b', 7_888)),
            new ChatMessage(ChatRole.User, "Middle three. " + new string('c', 7_888)),
            new ChatMessage(ChatRole.User, "how's it going?")
        };

        var text = ReviewTranscript.Render(items);

        Assert.Contains("Middle one.", text, StringComparison.Ordinal);
        Assert.Contains("Middle two.", text, StringComparison.Ordinal);
        Assert.Contains("Middle three.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewTranscript.OmittedNote, text, StringComparison.Ordinal);
        Assert.DoesNotContain("[truncated]", text, StringComparison.Ordinal);
    }
}
