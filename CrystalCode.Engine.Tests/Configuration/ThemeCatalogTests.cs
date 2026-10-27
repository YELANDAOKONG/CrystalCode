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
}
