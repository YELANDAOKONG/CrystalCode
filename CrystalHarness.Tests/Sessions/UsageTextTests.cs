using Crystal;

using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

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
}
