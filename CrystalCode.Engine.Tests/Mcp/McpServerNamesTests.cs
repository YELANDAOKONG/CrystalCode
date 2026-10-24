using CrystalCode.Engine.Mcp;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpServerNamesTests
{
    [Theory]
    [InlineData("abc", true)]
    [InlineData("local_filesystem", true)]
    [InlineData("my-server-42", true)]
    [InlineData("", false)]
    [InlineData("1starts_with_digit", false)]
    [InlineData("has__double", false)]
    [InlineData("ends_", false)]
    [InlineData("very_long_name_that_exceeds_maximum_length_limit", false)]
    public void IsServerName_ValidatesFormat(string input, bool expected)
    {
        Assert.Equal(expected, McpServerNames.IsServerName(input));
    }

    [Theory]
    [InlineData("test", "echo", "mcp__test__echo")]
    [InlineData("fs", "read_file", "mcp__fs__read_file")]
    [InlineData("srv", "tool-with-dash", "mcp__srv__tool-with-dash")]
    [InlineData("srv", "has.dot", "mcp__srv__has_dot")]
    [InlineData("srv", "has space", "mcp__srv__has_space")]
    public void TryToolName_BuildsValidName(string server, string tool, string expected)
    {
        var success = McpServerNames.TryToolName(server, tool, out var actual);

        Assert.True(success);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryToolName_ReturnsFalse_WhenToolNameIsEmpty()
    {
        var success = McpServerNames.TryToolName("test", "", out var name);

        Assert.False(success);
        Assert.Empty(name);
    }

    [Fact]
    public void TryToolName_ReturnsFalse_WhenResultIsTooLong()
    {
        var success = McpServerNames.TryToolName(
            "server",
            new string('x', 60),
            out var name);

        Assert.False(success);
        Assert.Empty(name);
    }
}
