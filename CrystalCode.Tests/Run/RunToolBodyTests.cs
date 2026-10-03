using Crystal.Tools;

using CrystalCode.Run;

using Xunit;

namespace CrystalCode.Tests.Run;

public sealed class RunToolBodyTests
{
    [Fact]
    public void Read_KeepsTheHeadAndCountsTheRest()
    {
        var text = Lines("R", 14);
        var body = RunToolBody.Format("read", ToolResultStatus.Success, text);

        Assert.Contains("R00", body, StringComparison.Ordinal);
        Assert.Contains("R11", body, StringComparison.Ordinal);
        Assert.DoesNotContain("R12", body, StringComparison.Ordinal);
        Assert.Contains("... 2 lines omitted", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_UsesASingularOmission()
    {
        var body = RunToolBody.Format("grep", ToolResultStatus.Success, Lines("G", 13));

        Assert.Contains("... 1 line omitted", body, StringComparison.Ordinal);
        Assert.DoesNotContain("lines omitted", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFailure_KeepsTheFullText()
    {
        var text = Lines("E", 14);
        var body = RunToolBody.Format("read", ToolResultStatus.Failure, text);

        Assert.Equal(text, body);
    }

    [Fact]
    public void Edit_KeepsTheFullText()
    {
        var text = Lines("D", 20);
        var body = RunToolBody.Format("edit", ToolResultStatus.Success, text);

        Assert.Equal(text, body);
    }

    [Fact]
    public void Bash_KeepsTheExitStatusAndTail()
    {
        var body = RunToolBody.Format(
            "bash",
            ToolResultStatus.Success,
            "exit 0\n" + Lines("L", 20));

        Assert.StartsWith("exit 0\n", body, StringComparison.Ordinal);
        Assert.Contains("... 12 earlier lines omitted", body, StringComparison.Ordinal);
        Assert.Contains("L12", body, StringComparison.Ordinal);
        Assert.Contains("L19", body, StringComparison.Ordinal);
        Assert.DoesNotContain("L11", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BashFailure_KeepsALongerTail()
    {
        var body = RunToolBody.Format(
            "bash",
            ToolResultStatus.Failure,
            "exit 1\n" + Lines("L", 40));

        Assert.StartsWith("exit 1\n", body, StringComparison.Ordinal);
        Assert.Contains("... 8 earlier lines omitted", body, StringComparison.Ordinal);
        Assert.Contains("L08", body, StringComparison.Ordinal);
        Assert.Contains("L39", body, StringComparison.Ordinal);
        Assert.DoesNotContain("L07", body, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherSuccess_KeepsTheHead()
    {
        var body = RunToolBody.Format("skill", ToolResultStatus.Success, Lines("S", 14));

        Assert.Contains("S00", body, StringComparison.Ordinal);
        Assert.DoesNotContain("S12", body, StringComparison.Ordinal);
        Assert.Contains("... 2 lines omitted", body, StringComparison.Ordinal);
    }

    private static string Lines(string prefix, int count)
    {
        var lines = new string[count];
        for (var index = 0; index < count; index++)
        {
            lines[index] = prefix + index.ToString("00");
        }

        return string.Join('\n', lines);
    }
}
