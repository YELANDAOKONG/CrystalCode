using System.Reflection;
using System.Runtime.InteropServices;

using CrystalCode.Commands;
using CrystalCode.Engine.Version;

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

        var text = writer.ToString();
        Assert.EndsWith(Environment.NewLine, text);
        var sections = text[..^Environment.NewLine.Length]
            .Split(Environment.NewLine + Environment.NewLine);
        Assert.Equal(2, sections.Length);

        var build = sections[0].Split(Environment.NewLine);
        var host = sections[1].Split(Environment.NewLine);
        Assert.Equal(4, build.Length);
        Assert.Equal(2, host.Length);
        Assert.Matches("^[0-9a-fA-F]{40}$", Value(build[0], "Crystal Code"));
        Assert.Matches("^[0-9a-fA-F]{40}$", Value(build[1], "Crystal"));
        Assert.Matches(@"^\S+$", Value(build[2], "SDK"));
        Assert.Equal(Configuration(typeof(CrystalCode.Program).Assembly), Value(build[3], "Configuration"));
        Assert.Matches(@"^\.NET .+$", Value(host[0], "Runtime"));
        Assert.Equal(
            HostOperatingSystem.Describe(
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture),
            Value(host[1], "OS"));
        Assert.DoesNotContain("1.0.0", text, StringComparison.Ordinal);
    }

    private static string Value(string line, string label)
    {
        Assert.StartsWith(label, line, StringComparison.Ordinal);
        return line[label.Length..].Trim();
    }

    private static string? Configuration(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
}
