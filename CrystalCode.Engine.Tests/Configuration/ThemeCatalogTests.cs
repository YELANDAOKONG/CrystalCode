using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class ThemeCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "crystal-theme-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void BuiltInNames_IgnoreCaseAndCanonicalize()
    {
        var home = new CrystalHome(_root);

        Assert.True(ThemeCatalog.IsBuiltIn("Light"));
        Assert.True(ThemeCatalog.Exists(home, "DARK"));
        Assert.Equal("light", ThemeCatalog.Canonical("LIGHT"));
        Assert.Equal("night", ThemeCatalog.Canonical("night"));
        Assert.Equal("light", HarnessSettings.CreateDefault().WithTheme("Light").Theme);
    }

    [Fact]
    public void Names_ListDefaultFirstThenFiles()
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(Path.Combine(home.ThemesDirectory, "night.json"), "{}");

        Assert.Equal(["default", "dark", "light", "night"], ThemeCatalog.Names(home));
        Assert.True(ThemeCatalog.Exists(home, "light"));
        Assert.True(ThemeCatalog.Exists(home, "night"));
        Assert.False(ThemeCatalog.Exists(home, "../night"));
    }

    [Fact]
    public void TryRead_ReturnsColorsAndAllowsComments()
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(Path.Combine(home.ThemesDirectory, "night.json"), "{ // c\n \"accent\": \"red\" }");

        Assert.True(ThemeCatalog.TryRead(home, "night", out var colors, out _));
        Assert.Equal("red", colors["accent"]);
    }

    [Fact]
    public void TryRead_FlattensOneLevelOfGroupedColors()
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(
            Path.Combine(home.ThemesDirectory, "night.json"),
            """{ "accent": "red", "approval": { "full": "orange1", "review": "blue" } }""");
        File.WriteAllText(
            Path.Combine(home.ThemesDirectory, "bad.json"),
            """{ "approval": { "full": 1 } }""");

        Assert.True(ThemeCatalog.TryRead(home, "night", out var colors, out _));
        Assert.Equal("red", colors["accent"]);
        Assert.Equal("orange1", colors["approval.full"]);
        Assert.Equal("blue", colors["approval.review"]);
        Assert.False(ThemeCatalog.TryRead(home, "bad", out _, out var error));
        Assert.Contains("approval.full", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadGauges_ReadsRangesInOrderWithDefaults()
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(
            Path.Combine(home.ThemesDirectory, "night.json"),
            """
            { "accent": "red", "gauges": { "context": [
              { "from": 90, "color": "red bold" },
              { "from": 50, "to": 89.9, "color": "yellow" },
              { "to": 10, "color": "green" } ] } }
            """);

        Assert.True(ThemeCatalog.TryRead(home, "night", out var colors, out _));
        Assert.True(ThemeCatalog.TryReadGauges(home, "night", out var gauges, out var error), error);

        Assert.Equal(["accent"], colors.Keys);
        Assert.Equal(
            [Range(90, 100, "red bold"), Range(50, 89.9, "yellow"), Range(0, 10, "green")],
            gauges["context"],
            new RuleComparer());
        Assert.True(ThemeCatalog.TryReadGauges(home, "light", out var none, out _));
        Assert.Empty(none);
    }

    [Fact]
    public void TryReadGauges_ReadsValueRules()
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(
            Path.Combine(home.ThemesDirectory, "night.json"),
            """
            { "gauges": { "thinking": [
              { "match": "off", "color": "dim" },
              { "match": ["high", "xhigh"], "color": "orange1" },
              { "regex": "^m", "color": "red" },
              { "match": "*", "color": "yellow" } ] } }
            """);

        Assert.True(ThemeCatalog.TryReadGauges(home, "night", out var gauges, out var error), error);

        Assert.Equal(
            [
                new ThemeGaugeRule(null, null, ["off"], null, "dim"),
                new ThemeGaugeRule(null, null, ["high", "xhigh"], null, "orange1"),
                new ThemeGaugeRule(null, null, [], "^m", "red"),
                new ThemeGaugeRule(null, null, ["*"], null, "yellow")
            ],
            gauges["thinking"],
            new RuleComparer());
    }

    [Theory]
    [InlineData("""{ "gauges": { "t": [{ "match": "a", "regex": "b", "color": "red" }] } }""", "not both")]
    [InlineData("""{ "gauges": { "t": [{ "match": "a", "from": 1, "color": "red" }] } }""", "not both")]
    [InlineData("""{ "gauges": { "t": [{ "match": [], "color": "red" }] } }""", "match")]
    [InlineData("""{ "gauges": { "t": [{ "match": [1], "color": "red" }] } }""", "match")]
    [InlineData("""{ "gauges": { "t": [{ "regex": "", "color": "red" }] } }""", "regex")]
    public void TryReadGauges_ReportsBadValueRules(string json, string expected)
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(Path.Combine(home.ThemesDirectory, "bad.json"), json);

        Assert.False(ThemeCatalog.TryReadGauges(home, "bad", out _, out var error));

        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "gauges": [] }""", "object")]
    [InlineData("""{ "gauges": { "context": {} } }""", "list")]
    [InlineData("""{ "gauges": { "context": [1] } }""", "objects")]
    [InlineData("""{ "gauges": { "context": [{ "from": 1 }] } }""", "color")]
    [InlineData("""{ "gauges": { "context": [{ "from": "1", "color": "red" }] } }""", "from")]
    [InlineData("""{ "gauges": { "context": [{ "to": "9", "color": "red" }] } }""", "to")]
    [InlineData("""{ "gauges": { "context": [{ "to": null, "color": "red" }] } }""", "to")]
    [InlineData("""{ "gauges": { "context": [{ "from": 1e999, "color": "red" }] } }""", "from")]
    [InlineData("""{ "gauges": { "context": [{ "color": "red", "extra": 1 }] } }""", "extra")]
    public void TryReadGauges_ReportsBadShapes(string json, string expected)
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(Path.Combine(home.ThemesDirectory, "bad.json"), json);

        Assert.False(ThemeCatalog.TryReadGauges(home, "bad", out _, out var error));

        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_ReportsBadFiles()
    {
        var home = new CrystalHome(_root);
        Directory.CreateDirectory(home.ThemesDirectory);
        File.WriteAllText(Path.Combine(home.ThemesDirectory, "bad.json"), "[1]");
        File.WriteAllText(Path.Combine(home.ThemesDirectory, "worse.json"), "{");

        Assert.False(ThemeCatalog.TryRead(home, "bad", out _, out var shape));
        Assert.False(ThemeCatalog.TryRead(home, "worse", out _, out var syntax));
        Assert.False(ThemeCatalog.TryRead(home, "missing", out _, out var missing));
        Assert.Contains("object", shape, StringComparison.Ordinal);
        Assert.Contains("worse", syntax, StringComparison.Ordinal);
        Assert.Contains("not found", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_RoundTripsThroughConfigAndLaunchOverride()
    {
        var home = new CrystalHome(_root);
        var store = new DiskSettingsStore(home);
        var settings = store.LoadOrCreate();
        Assert.Equal("default", settings.Theme);

        store.Save(settings.WithTheme("night"));
        Assert.Equal("night", store.LoadOrCreate().Theme);

        Assert.True(SessionLaunchOverrides.TryApply(
            settings, new SessionLaunchOptions { Theme = "other" }, out var applied, out _, out _));
        Assert.Equal("other", applied.Theme);
        Assert.Equal("night", store.LoadOrCreate().Theme);
    }

    [Fact]
    public void WithTheme_RejectsUnsafeNames()
    {
        var settings = new DiskSettingsStore(new CrystalHome(_root)).LoadOrCreate();

        Assert.Throws<ArgumentException>(() => settings.WithTheme("../x"));
    }

    private static ThemeGaugeRule Range(double from, double to, string color) =>
        new(from, to, [], null, color);

    private sealed class RuleComparer : IEqualityComparer<ThemeGaugeRule>
    {
        public bool Equals(ThemeGaugeRule? x, ThemeGaugeRule? y) =>
            x is not null && y is not null
            && x.From == y.From && x.To == y.To && x.Pattern == y.Pattern && x.Color == y.Color
            && x.Match.SequenceEqual(y.Match);

        public int GetHashCode(ThemeGaugeRule obj) => obj.Color.GetHashCode(StringComparison.Ordinal);
    }
}
