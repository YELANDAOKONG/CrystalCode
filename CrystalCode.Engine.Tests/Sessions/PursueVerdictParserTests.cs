using CrystalCode.Engine.Sessions.Pursue;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PursueVerdictParserTests
{
    private static readonly HashSet<int> Entries = [1, 2, 3];

    [Fact]
    public void Parse_AcceptsDoneWithShownEvidenceAndAFence()
    {
        var text = "```json\n{\"verdict\":\"done\",\"reason\":\"ok\",\"evidence\":[2]}\n```";

        Assert.True(PursueVerdictParser.TryParse(text, Entries, out var verdict, out _));
        Assert.Equal(PursueVerdictKind.Done, verdict.Kind);
        Assert.Equal([2], verdict.Evidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("done")]
    [InlineData("[1]")]
    [InlineData("{\"verdict\":\"finished\",\"reason\":\"x\"}")]
    [InlineData("{\"verdict\":\"done\",\"reason\":\"x\"}")]
    [InlineData("{\"verdict\":\"done\",\"reason\":\"x\",\"evidence\":[9]}")]
    [InlineData("{\"verdict\":\"done\",\"reason\":\" \",\"evidence\":[1]}")]
    [InlineData("{\"verdict\":\"continue\",\"reason\":\"x\"}")]
    [InlineData("{\"verdict\":\"wait\",\"reason\":\"x\"}")]
    [InlineData("{\"verdict\":\"wait\",\"reason\":\"x\",\"waitFor\":\"task_1\"}")]
    public void Parse_RejectsAnythingOffContract(string text)
    {
        Assert.False(PursueVerdictParser.TryParse(text, Entries, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Parse_AcceptsBlockedAndWait()
    {
        Assert.True(PursueVerdictParser.TryParse("{\"verdict\":\"blocked\",\"reason\":\"no key\"}", Entries, out var blocked, out _));
        Assert.Equal(PursueVerdictKind.Blocked, blocked.Kind);
        Assert.True(PursueVerdictParser.TryParse(
            "{\"verdict\":\"wait\",\"reason\":\"build\",\"waitFor\":[\"task_1\"]}", Entries, out var wait, out _));
        Assert.Equal(["task_1"], wait.WaitFor);
    }
}
