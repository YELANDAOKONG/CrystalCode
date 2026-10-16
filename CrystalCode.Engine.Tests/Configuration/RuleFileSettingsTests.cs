using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class RuleFileSettingsTests
{
    [Fact]
    public void ParseLaunch_OnSetsBothSides()
    {
        var current = new RuleFileSettings(globalEnabled: false, projectEnabled: true);

        var applied = RuleFileSettings.ParseLaunch(" on ", current);

        Assert.True(applied.Global);
        Assert.True(applied.Project);
    }

    [Fact]
    public void ParseLaunch_AssignmentKeepsTheOtherSide()
    {
        var current = RuleFileSettings.Default.WithProject(false);

        var applied = RuleFileSettings.ParseLaunch("global=off, project=ON", current);

        Assert.False(applied.Global);
        Assert.True(applied.Project);
    }

    [Theory]
    [InlineData("")]
    [InlineData("global=off,global=on")]
    [InlineData("home=off")]
    [InlineData("project")]
    public void ParseLaunch_RejectsInvalidValues(string text)
    {
        var error = Assert.Throws<ArgumentException>(
            () => RuleFileSettings.ParseLaunch(text, RuleFileSettings.Default));

        Assert.Contains("--rules", error.Message, StringComparison.Ordinal);
    }
}
