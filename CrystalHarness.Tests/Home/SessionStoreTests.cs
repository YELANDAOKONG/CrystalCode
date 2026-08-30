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

    [Fact]
    public void Save_ThenLoad_RoundTripsUsageAndCounts()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(
            new SessionDocument
            {
                Id = "cccc",
                Workspace = "/tmp/one",
                UserTurns = 3,
                ModelCalls = 4,
                ToolCalls = 7,
                Usage = new SessionUsageDocument
                {
                    InputTokenCount = 1200,
                    OutputTokenCount = 80,
                    ReasoningTokenCount = 12
                },
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hi" }]
            });

        Assert.True(store.TryLoad("cccc", out var loaded));
        Assert.Equal(3, loaded.UserTurns);
        Assert.Equal(4, loaded.ModelCalls);
        Assert.Equal(7, loaded.ToolCalls);
        Assert.NotNull(loaded.Usage);
        Assert.Equal(1200, loaded.Usage.InputTokenCount);
        Assert.Equal(80, loaded.Usage.OutputTokenCount);
        Assert.Equal(12, loaded.Usage.ReasoningTokenCount);
    }

    [Fact]
    public void TryLoad_MissingUsageRemainsNull()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(
            new SessionDocument
            {
                Id = "dddd",
                Workspace = "/tmp/one",
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hi" }]
            });

        Assert.True(store.TryLoad("dddd", out var loaded));
        Assert.Null(loaded.Usage);
        Assert.Equal(0, loaded.UserTurns);
    }
}
