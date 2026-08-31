using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class HarnessExceptionMapperTests
{
    [Fact]
    public async Task MapAsync_ReturnsFailureForIoException()
    {
        var output = await HarnessExceptionMapper.MapAsync(
            new ToolCall("1", ReadTool.ToolName, "{}"),
            new IOException("disk full"));

        Assert.NotNull(output);
        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Tool read failed: disk full", output.Text);
    }
}
