using CrystalCode.Engine.Mcp;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpEnvironmentTextTests
{
    [Fact]
    public void TryExpand_ExpandsVariables()
    {
        Func<string, string?> lookup = name => name == "FOO" ? "bar" : null;

        var success = McpEnvironmentText.TryExpand("prefix_${FOO}_suffix", lookup, out var expanded, out _);

        Assert.True(success);
        Assert.Equal("prefix_bar_suffix", expanded);
    }

    [Fact]
    public void TryExpand_ExpandsMultipleVariables()
    {
        var env = new Dictionary<string, string> { ["FOO"] = "1", ["BAR"] = "2" };

        var success = McpEnvironmentText.TryExpand(
            "${FOO}+${BAR}",
            name => env.GetValueOrDefault(name),
            out var expanded,
            out _);

        Assert.True(success);
        Assert.Equal("1+2", expanded);
    }

    [Fact]
    public void TryExpand_ReturnsFalse_WhenVariableIsMissing()
    {
        Func<string, string?> lookup = _ => null;

        var success = McpEnvironmentText.TryExpand("need_${MISSING}", lookup, out var expanded, out var missing);

        Assert.False(success);
        Assert.Equal("MISSING", missing);
        Assert.Equal("need_${MISSING}", expanded);
    }

    [Fact]
    public void TryExpand_TreatsEmptyAsSet()
    {
        Func<string, string?> lookup = _ => string.Empty;

        var success = McpEnvironmentText.TryExpand("${EMPTY}", lookup, out var expanded, out _);

        Assert.True(success);
        Assert.Equal(string.Empty, expanded);
    }

    [Fact]
    public void TryExpand_LeavesTextUnchanged_WhenNoVariables()
    {
        Func<string, string?> lookup = _ => throw new InvalidOperationException("Should not be called.");

        var success = McpEnvironmentText.TryExpand("plain text", lookup, out var expanded, out _);

        Assert.True(success);
        Assert.Equal("plain text", expanded);
    }
}
