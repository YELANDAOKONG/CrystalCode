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
            name => (string)typeof(Theme).GetProperty(char.ToUpperInvariant(name[0]) + name[1..])!.GetValue(null)!);

        Theme.Reset();

        Assert.True(Theme.TryApply(colors, out var error), error);
    }
}
