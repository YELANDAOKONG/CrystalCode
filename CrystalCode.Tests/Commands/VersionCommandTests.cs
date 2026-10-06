using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class VersionCommandTests
{
    [Theory]
    [InlineData("--version", true)]
    [InlineData("version", false)]
    [InlineData("--Version", false)]
    public void IsVersionRequest_MatchesOnlyTheExactFlag(string argument, bool expected)
    {
        Assert.Equal(expected, VersionCommand.IsVersionRequest([argument]));
    }

    [Fact]
    public void IsVersionRequest_RejectsExtraArguments()
    {
        Assert.False(VersionCommand.IsVersionRequest(["--version", "--help"]));
        Assert.False(VersionCommand.IsVersionRequest([]));
    }

    [Fact]
    public void Write_PrintsBuildIdentityWithoutAssemblyVersion()
    {
        var writer = new StringWriter();

        VersionCommand.Write(writer);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Matches("^Crystal Code  [0-9a-fA-F]{40}$", lines[0]);
        Assert.Matches("^Crystal\\s{2,}[0-9a-fA-F]{40}$", lines[1]);
        Assert.Matches("^SDK\\s{2,}\\S+$", lines[2]);
        Assert.Matches("^Runtime\\s{2,}\\.NET .+$", lines[3]);
        Assert.DoesNotContain("1.0.0", writer.ToString(), StringComparison.Ordinal);
    }
}
