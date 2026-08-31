using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class QuestionToolTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsUserAnswer()
    {
        var prompt = new FixedUserPrompt("use tests");
        var tool = new QuestionTool(prompt);

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                QuestionTool.ToolName,
                """{"question":"How should this be verified?","options":["use tests","skip"]}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("use tests", output.Text);
        Assert.Equal("How should this be verified?", prompt.LastQuestion);
        Assert.Equal(["use tests", "skip"], prompt.LastOptions);
    }
}
