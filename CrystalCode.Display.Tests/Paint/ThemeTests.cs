using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

[Collection("Theme")]
public sealed class ThemeTests : IDisposable
{
    public ThemeTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Fact]
    public void TryApply_OverridesOnlyNamedColors()
    {
        var applied = Theme.TryApply(new Dictionary<string, string> { ["accent"] = "red" }, out _);

        Assert.True(applied);
        Assert.Equal("red", Theme.Accent);
        Assert.Equal("grey50", Theme.Chrome);
    }

    [Fact]
    public void TryApply_RejectsUnknownNamesAndBadStylesWithoutChange()
    {
        Assert.False(Theme.TryApply(new Dictionary<string, string> { ["nope"] = "red" }, out var unknown));
        Assert.False(Theme.TryApply(new Dictionary<string, string> { ["accent"] = "not-a-color" }, out var bad));
        Assert.False(Theme.TryApply(new Dictionary<string, string> { ["accent"] = "red][/" }, out _));

        Assert.Contains("nope", unknown, StringComparison.Ordinal);
        Assert.Contains("accent", bad, StringComparison.Ordinal);
        Assert.Equal("lightsteelblue", Theme.Accent);
    }

    [Theory]
    [InlineData(0, "grey50")]
    [InlineData(59.9, "grey50")]
    [InlineData(60, "tan")]
    [InlineData(79.9, "tan")]
    [InlineData(80, "indianred")]
    [InlineData(100, "indianred")]
    public void ForContext_DefaultsToWarningAndFailThresholds(double percent, string expected)
    {
        Theme.Reset();

        Assert.Equal(expected, Theme.ForContext(percent));
    }

    [Fact]
    public void ForContext_FirstMatchingRangeWinsAndGapsUseChrome()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeBand>>
        {
            ["context"] = [new GaugeBand(90, 100, "red"), new GaugeBand(50, 100, "yellow"), new GaugeBand(0, 10, "green")]
        };

        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);

        Assert.Equal("red", Theme.ForContext(95));
        Assert.Equal("yellow", Theme.ForContext(70));
        Assert.Equal("green", Theme.ForContext(5));
        Assert.Equal("grey50", Theme.ForContext(30));
        Theme.Reset();
        Assert.Equal("indianred", Theme.ForContext(95));
    }

    [Theory]
    [InlineData("other", 0, 100, "red")]
    [InlineData("context", -1, 100, "red")]
    [InlineData("context", 0, 101, "red")]
    [InlineData("context", 60, 50, "red")]
    [InlineData("context", 0, 100, "not-a-color")]
    public void TryApply_RejectsBadGauges(string gauge, double from, double to, string style)
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeBand>>
        {
            [gauge] = [new GaugeBand(from, to, style)]
        };

        Assert.False(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error));

        Assert.Contains(gauge == "other" ? "gauge" : "context", error, StringComparison.Ordinal);
        Assert.Equal("grey50", Theme.ForContext(10));
    }

    [Fact]
    public void TryApply_RejectsAnApprovalColorThatIsNotGrouped()
    {
        Assert.False(Theme.TryApply(new Dictionary<string, string> { ["approval"] = "red" }, out var error));

        Assert.Contains("approval", error, StringComparison.Ordinal);
        Assert.Equal("darkorange", Theme.ApprovalFull);
    }

    [Theory]
    [InlineData("code", "red on blue")]
    [InlineData("codeBg", "red on blue")]
    public void TryApply_RejectsCodeColorsThatBreakTheCodeBlockMarkup(string token, string value)
    {
        Assert.False(Theme.TryApply(new Dictionary<string, string> { [token] = value }, out var error));

        Assert.Contains("codeBg", error, StringComparison.Ordinal);
        Assert.Equal("grey23", Theme.CodeBg);
    }

    [Fact]
    public void ForApproval_OnlyFullDiffersFromChromeByDefault()
    {
        Assert.Equal(Theme.Chrome, Theme.ForApproval("Review"));
        Assert.Equal(Theme.Chrome, Theme.ForApproval("Unknown"));
        Assert.Equal("darkorange", Theme.ForApproval("Full"));

        Theme.TryApply(new Dictionary<string, string> { ["approval.audit"] = "red" }, out _);

        Assert.Equal("red", Theme.ForApproval("Audit"));
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        Theme.TryApply(new Dictionary<string, string> { ["accent"] = "red" }, out _);

        Theme.Reset();

        Assert.Equal("lightsteelblue", Theme.Accent);
    }

    [Fact]
    public void BuiltIns_DefaultEqualsDarkAndLightDiffers()
    {
        Assert.True(Theme.TryUseBuiltIn("light"));
        var light = Theme.Accent;
        Assert.True(Theme.TryUseBuiltIn("dark"));
        var dark = Theme.Accent;
        Assert.True(Theme.TryUseBuiltIn("default"));

        Assert.Equal(dark, Theme.Accent);
        Assert.NotEqual(light, dark);
        Assert.True(Theme.TryUseBuiltIn("LIGHT"));
        Assert.False(Theme.TryUseBuiltIn("night"));
    }

    [Fact]
    public void LightPalette_UsesOnlyValidStyles()
    {
        Theme.TryUseBuiltIn("light");
        var colors = Theme.TokenNames.ToDictionary(
            name => name,
            name => (string)typeof(Theme).GetProperty(PropertyName(name))!.GetValue(null)!);

        Theme.Reset();

        Assert.True(Theme.TryApply(colors, out var error), error);
    }

    private static string PropertyName(string token) =>
        string.Concat(token.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
