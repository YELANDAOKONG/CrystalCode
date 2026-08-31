using Crystal;

using CrystalHarness.Display.Paint;

using Xunit;

namespace CrystalHarness.Tests.Display.Paint;

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

        Assert.StartsWith("CTX ", text, StringComparison.Ordinal);
        Assert.Contains("%", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx", text, StringComparison.Ordinal);
    }
}
