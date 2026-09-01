using Crystal;

using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class UsageTextTests
{
    [Fact]
    public void Format_MissingUsageIsUppercasePlaceholder()
    {
        Assert.Equal("CTX --", UsageText.Format(null, 128000));
    }

    [Fact]
    public void Format_IncludesUppercaseLabelAndPercent()
    {
        var text = UsageText.Format(new TokenUsage(100, 20), 1000);

        Assert.Equal("CTX 12%  ·  100 IN / 20 OUT", text);
        Assert.DoesNotContain("ctx", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" in ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" out", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_CapsPercentAtOneHundred()
    {
        Assert.Equal("CTX 100%  ·  200 IN / 1 OUT", UsageText.Format(new TokenUsage(200, 1), 100));
    }

    [Fact]
    public void FormatTotal_SumsInputAndOutput()
    {
        Assert.Equal(string.Empty, UsageText.FormatTotal(null));
        Assert.Equal("120 Total", UsageText.FormatTotal(new TokenUsage(100, 20)));
        Assert.Equal("782.9k Total", UsageText.FormatTotal(new TokenUsage(769_100, 13_800)));
    }

    [Fact]
    public void FormatEstimate_PrefixesTildeAndTitleCaseTokens()
    {
        Assert.Equal("~0 Tokens", UsageText.FormatEstimate(0));
        Assert.Equal("~12 Tokens", UsageText.FormatEstimate(12));
        Assert.Equal("~10k Tokens", UsageText.FormatEstimate(10_000));
    }
}
