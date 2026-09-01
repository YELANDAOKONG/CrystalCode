using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class QuestionToolTests
{
    [Fact]
    public async Task InvokeAsync_ValidQuestions_ReturnsOrderedAnswers()
    {
        var prompt = new FixedUserPrompt(
            new QuestionResponse(
                [["Build", "Tests"], ["Keep it local"]],
                IsRejected: false));
        var tool = new QuestionTool(prompt);

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                QuestionTool.ToolName,
                """
                {
                  "questions": [
                    {
                      "header": "Verification",
                      "question": "What should run?",
                      "options": [
                        { "label": "Build", "description": "Compile the solution." },
                        { "label": "Tests", "description": "Run the test suite." }
                      ],
                      "multiple": true,
                      "custom": false
                    },
                    {
                      "header": "Notes",
                      "question": "Anything else?",
                      "options": []
                    }
                  ]
                }
                """));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            "User answers: {\"answers\":[[\"Build\",\"Tests\"],[\"Keep it local\"]]}",
            output.Text);
        Assert.NotNull(prompt.LastQuestions);
        Assert.Collection(
            prompt.LastQuestions,
            question =>
            {
                Assert.Equal("Verification", question.Header);
                Assert.Equal("What should run?", question.Text);
                Assert.True(question.Multiple);
                Assert.False(question.Custom);
                Assert.Collection(
                    question.Options,
                    option => Assert.Equal(
                        new QuestionOption("Build", "Compile the solution."),
                        option),
                    option => Assert.Equal(
                        new QuestionOption("Tests", "Run the test suite."),
                        option));
            },
            question =>
            {
                Assert.Equal("Notes", question.Header);
                Assert.False(question.Multiple);
                Assert.True(question.Custom);
                Assert.Empty(question.Options);
            });
    }

    [Fact]
    public async Task InvokeAsync_DismissedQuestion_ReturnsFailure()
    {
        var prompt = new FixedUserPrompt(new QuestionResponse([], IsRejected: true));
        var tool = new QuestionTool(prompt);

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                QuestionTool.ToolName,
                """
                {
                  "questions": [{
                    "header": "Choice",
                    "question": "Pick one.",
                    "options": [{ "label": "A", "description": "Choose A." }]
                  }]
                }
                """));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("The user dismissed the question.", output.Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"questions\":[]}")]
    [InlineData("{\"questions\":[{\"header\":\"Choice\",\"question\":\"Pick.\",\"options\":[],\"custom\":false}]}")]
    [InlineData("{\"questions\":[{\"header\":\"Choice\",\"question\":\"Pick.\",\"options\":[{\"label\":\"A\",\"description\":\"First\"},{\"label\":\"A\",\"description\":\"Second\"}]}]}")]
    public async Task InvokeAsync_InvalidQuestions_ReturnsFailure(string arguments)
    {
        var tool = new QuestionTool(new FixedUserPrompt("unused"));

        var output = await tool.InvokeAsync(
            new ToolCall("1", QuestionTool.ToolName, arguments));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
    }
}
