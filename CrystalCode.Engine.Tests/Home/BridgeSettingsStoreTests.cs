using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class BridgeSettingsStoreTests
{
    [Fact]
    public void Default_IsNotWritten()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);

        store.Save(store.LoadOrCreate());

        Assert.DoesNotContain("\"bridge\"", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
        Assert.True(store.Load().Bridge.IsDefault);
    }

    [Fact]
    public void Custom_RoundTripsAndSurvivesAnotherSave()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Insert(root, """
            "bridge": { "customConfigOptions": true, "configOptions": ["model", "approval"] },
            """);

        store.Save(store.Load().WithApproval(ApprovalMode.Edit));
        var reloaded = store.Load();

        Assert.True(reloaded.Bridge.Custom);
        Assert.Equal(["model", "approval"], reloaded.Bridge.Effective);
        Assert.Contains("\"bridge\"", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ASavedListStaysWhileCustomIsOff()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Insert(root, """
            "bridge": { "configOptions": ["model"] },
            """);

        store.Save(store.Load());
        var reloaded = store.Load();

        Assert.False(reloaded.Bridge.Custom);
        Assert.Equal(["model"], reloaded.Bridge.Options);
        Assert.Equal(["mode", "model", "thinking", "approval", "agents"], reloaded.Bridge.Effective);
    }

    [Fact]
    public void CustomWithAll_OffersEveryOption()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Insert(root, """
            "bridge": { "customConfigOptions": true, "configOptions": ["all"] },
            """);

        var loaded = store.Load();
        store.Save(loaded);

        Assert.Equal(13, store.Load().Bridge.Effective.Count);
    }

    [Fact]
    public void CustomWithoutAList_UsesTheBuiltInList()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Insert(root, """
            "bridge": { "customConfigOptions": true },
            """);

        store.Save(store.Load());

        Assert.True(store.Load().Bridge.Custom);
        Assert.Equal(["mode", "model", "thinking", "approval", "agents"], store.Load().Bridge.Effective);
    }

    [Fact]
    public void WorkspaceTrust_IsOffByDefaultAndRoundTripsWhenOn()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Assert.False(store.Load().Bridge.WorkspaceTrust);
        Insert(root, """
            "bridge": { "workspaceTrust": true },
            """);

        store.Save(store.Load());

        Assert.True(store.Load().Bridge.WorkspaceTrust);
        Assert.False(store.Load().Bridge.IsDefault);
    }

    [Theory]
    [InlineData("""{ "customConfigOptions": true, "configOptions": ["nope"] }""", "nope")]
    [InlineData("""{ "customConfigOptions": true, "configOptions": [] }""", "at least one")]
    [InlineData("""{ "customConfigOptions": true, "configOptions": [" "] }""", "empty")]
    public void Load_RejectsInvalidLists(string section, string expected)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Insert(root, "\"bridge\": " + section + ",");

        var exception = Assert.Throws<InvalidOperationException>(store.Load);

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static void Insert(TemporaryHome root, string text) =>
        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace("{", "{\n" + text, StringComparison.Ordinal));
}
