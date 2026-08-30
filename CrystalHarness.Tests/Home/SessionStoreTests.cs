using CrystalHarness.Home;

using Xunit;

namespace CrystalHarness.Tests.Home;

public sealed class SessionStoreTests
{
    [Fact]
    public void Save_ThenLoadLatestForWorkspace()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        var first = new SessionDocument
        {
            Id = "aaaa",
            Workspace = "/tmp/one",
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "old" }]
        };
        var second = new SessionDocument
        {
            Id = "bbbb",
            Workspace = "/tmp/one",
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "new" }]
        };
        store.Save(first);
        Thread.Sleep(20);
        store.Save(second);

        Assert.True(store.TryLoadLatest("/tmp/one", out var latest));
        Assert.Equal("bbbb", latest.Id);
        Assert.True(store.TryLoad("aaaa", out var loaded));
        Assert.Equal("old", loaded.Items[0].Text);
    }

    [Fact]
    public void TryLoad_IsFalseWhenMissing()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);

        Assert.False(store.TryLoad("missing", out _));
    }
}
