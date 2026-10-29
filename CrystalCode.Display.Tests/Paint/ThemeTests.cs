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
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["context"] = [Range(90, 100, "red"), Range(50, 100, "yellow"), Range(0, 10, "green")]
        };

        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);

        Assert.Equal("red", Theme.ForContext(95));
        Assert.Equal("yellow", Theme.ForContext(70));
        Assert.Equal("green", Theme.ForContext(5));
        Assert.Equal("grey50", Theme.ForContext(30));
        Theme.Reset();
        Assert.Equal("indianred", Theme.ForContext(95));
    }

    [Fact]
    public void ForValue_IsChromeWithoutRules()
    {
        Theme.Reset();

        Assert.Equal("grey50", Theme.ForValue("thinking", "High"));
        Assert.Equal("grey50", Theme.ForValue("tier", "Priority"));
        Assert.Equal("grey50", Theme.ForValue("speed", "Fast"));
    }

    [Fact]
    public void ForValue_UsesTheFallbackWhenNoRuleHoldsTheValue()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["model"] = [Value(["gpt-5"], null, "green")]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out _));

        Assert.Equal("green", Theme.ForValue("model", "gpt-5", Theme.User));
        Assert.Equal(Theme.User, Theme.ForValue("model", "other", Theme.User));
        Assert.Equal(Theme.User, Theme.ForValue("prompt-set", "x", Theme.User));
        Assert.Equal(Theme.Chrome, Theme.ForValue("prompt-set", "x"));
        Theme.Reset();
    }

    [Theory]
    [InlineData("model")]
    [InlineData("context-window")]
    [InlineData("prompt-set")]
    public void TryApply_AcceptsValueRulesForEachValueGauge(string gauge)
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>> { [gauge] = [Value(["a"], null, "red")] };

        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);

        Assert.Equal("red", Theme.ForValue(gauge, "A"));
        Theme.Reset();
    }

    [Fact]
    public void ForValue_ContextWindowRangeNeedsANumberAndMatchRulesStillWork()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["context-window"] =
            [
                Value(["1M"], null, "gold1"),
                new GaugeRule(null, 99_999, [], null, "red"),
                new GaugeRule(100_000, 200_000, [], null, "yellow")
            ]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);

        Assert.Equal("gold1", Theme.ForValue("context-window", "1M", null, 1_000_000));
        Assert.Equal("red", Theme.ForValue("context-window", "64K", null, 64_000));
        Assert.Equal("yellow", Theme.ForValue("context-window", "200K", null, 200_000));
        Assert.Equal(Theme.Chrome, Theme.ForValue("context-window", "300K", null, 300_000));
        Assert.Equal(Theme.Chrome, Theme.ForValue("context-window", "64K"));
        Theme.Reset();
    }

    [Fact]
    public void TryApply_RejectsATokenSizedRangeOnTheContextPercentGauge()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["context"] = [new GaugeRule(200_000, null, [], null, "red")]
        };

        Assert.False(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error));

        Assert.Contains("context", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(200, 100)]
    [InlineData(double.NaN, 100)]
    public void TryApply_RejectsABadContextWindowRange(double from, double to)
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["context-window"] = [new GaugeRule(from, to, [], null, "red")]
        };

        Assert.False(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error));

        Assert.Contains("context-window", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ForValue_FirstRuleWinsAcrossPlainRegexAndWildcard()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["thinking"] =
            [
                Value(["off"], null, "dim"),
                Value(["HIGH", "xhigh"], null, "orange1"),
                Value([], "^m(ax|edium)", "red"),
                Value(["*"], null, "yellow")
            ]
        };

        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);

        Assert.Equal("dim", Theme.ForValue("thinking", "Off"));
        Assert.Equal("orange1", Theme.ForValue("thinking", "High"));
        Assert.Equal("orange1", Theme.ForValue("thinking", "Xhigh"));
        Assert.Equal("red", Theme.ForValue("thinking", "Max"));
        Assert.Equal("red", Theme.ForValue("thinking", "Medium"));
        Assert.Equal("yellow", Theme.ForValue("thinking", "Default"));
        Assert.Equal("grey50", Theme.ForValue("tier", "Priority"));
        Theme.Reset();
        Assert.Equal("grey50", Theme.ForValue("thinking", "High"));
    }

    [Fact]
    public void ForValue_WithoutAWildcardAnUnlistedValueUsesChrome()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["tier"] = [Value(["priority"], null, "gold1")]
        };

        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out _));

        Assert.Equal("gold1", Theme.ForValue("tier", "Priority"));
        Assert.Equal("grey50", Theme.ForValue("tier", "Flex"));
    }

    [Theory]
    [InlineData("thinking", "match", "(")]
    [InlineData("thinking", "bad-style", "")]
    [InlineData("tier", "range", "")]
    [InlineData("tier", "both", "")]
    [InlineData("tier", "empty", "")]
    [InlineData("context", "value", "")]
    public void TryApply_RejectsBadValueRules(string gauge, string kind, string pattern)
    {
        var rule = kind switch
        {
            "match" => Value([], pattern, "red"),
            "bad-style" => Value(["a"], null, "not-a-color"),
            "range" => new GaugeRule(0, 10, ["a"], null, "red"),
            "both" => Value(["a"], "a", "red"),
            "empty" => Value([], null, "red"),
            _ => Value(["a"], null, "red")
        };
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>> { [gauge] = [rule] };

        Assert.False(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error));

        Assert.Contains(gauge, error, StringComparison.Ordinal);
        Assert.Equal("grey50", Theme.ForValue(gauge, "a"));
    }

    [Theory]
    [InlineData("other", 0, 100, "red")]
    [InlineData("context", -1, 100, "red")]
    [InlineData("context", 0, 101, "red")]
    [InlineData("context", 60, 50, "red")]
    [InlineData("context", double.NaN, 50, "red")]
    [InlineData("context", 0, double.PositiveInfinity, "red")]
    [InlineData("context", double.NegativeInfinity, 50, "red")]
    [InlineData("context", 0, 100, "not-a-color")]
    public void TryApply_RejectsBadGauges(string gauge, double from, double to, string style)
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            [gauge] = [Range(from, to, style)]
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

    private static GaugeRule Range(double from, double to, string style) =>
        new(from, to, [], null, style);

    private static GaugeRule Value(string[] match, string? pattern, string style) =>
        new(null, null, match, pattern, style);

    private static string PropertyName(string token) =>
        string.Concat(token.Split('.').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
