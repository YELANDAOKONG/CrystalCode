using CrystalCode.Engine.Home.Settings;

using Xunit;

namespace CrystalCode.Engine.Tests.Home.Settings;

public sealed class WebSettingsStoreTests
{
    [Fact]
    public void Default_IsNotWrittenAndFallsBackToTheIndex()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);

        store.Save(store.LoadOrCreate());

        Assert.DoesNotContain("\"web\"", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
        Assert.True(store.Load().Web.IsDefault);
        Assert.True(store.Load().Web.SpaFallback);
    }

    [Fact]
    public void SpaFallbackOff_RoundTripsAndSurvivesAnotherSave()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());
        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace(
                "{",
                """
                {
                  "web": { "spaFallback": false },
                """,
                StringComparison.Ordinal));

        store.Save(store.Load());
        var reloaded = store.Load();

        Assert.False(reloaded.Web.SpaFallback);
        Assert.False(reloaded.Web.IsDefault);
        Assert.Contains("\"spaFallback\": false", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Auth_DefaultsAreNotWrittenAndOverridesRoundTrip()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());
        var defaults = store.Load();
        Assert.Equal("token", defaults.Server.Auth);
        Assert.Equal("none", defaults.Web.Auth);

        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace(
                "{",
                """
                {
                  "server": { "auth": "NONE" },
                  "web": { "auth": "token" },
                """,
                StringComparison.Ordinal));
        store.Save(store.Load());
        var reloaded = store.Load();

        Assert.Equal("none", reloaded.Server.Auth);
        Assert.Equal("token", reloaded.Web.Auth);
        Assert.True(reloaded.Web.SpaFallback);
        var text = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"auth\": \"none\"", text, StringComparison.Ordinal);
        Assert.Contains("\"auth\": \"token\"", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("web")]
    public void Auth_RejectsAnUnknownMode(string section)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());
        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace(
                "{", "{ \"" + section + "\": { \"auth\": \"basic\" },", StringComparison.Ordinal));

        var exception = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains(section + ".auth", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerSection_IsSeparateFromTheWebSection()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());
        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace(
                "{",
                """
                {
                  "server": { "allowOrigins": ["https://app.example.test"] },
                  "web": { "spaFallback": false },
                """,
                StringComparison.Ordinal));

        var loaded = store.Load();

        Assert.Equal(["https://app.example.test"], loaded.Server.AllowOrigins);
        Assert.False(loaded.Web.SpaFallback);
    }
}
