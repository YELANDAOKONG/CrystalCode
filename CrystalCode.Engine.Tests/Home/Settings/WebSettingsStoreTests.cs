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
