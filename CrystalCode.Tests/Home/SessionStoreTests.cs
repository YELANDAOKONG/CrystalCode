using CrystalCode.Home;

using Xunit;

namespace CrystalCode.Tests.Home;

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

    [Fact]
    public void List_FiltersWorkspaceAndSortsNewestFirst()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(
            new SessionDocument
            {
                Id = "older",
                Workspace = "/tmp/one",
                PlanMode = true,
                UserTurns = 2,
                Items =
                [
                    new SessionItemDocument
                    {
                        Kind = "message",
                        Role = "user",
                        Text = "first\nrequest"
                    }
                ]
            });
        Thread.Sleep(20);
        store.Save(
            new SessionDocument
            {
                Id = "newer",
                Workspace = "/tmp/one",
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "new" }]
            });
        store.Save(
            new SessionDocument
            {
                Id = "other",
                Workspace = "/tmp/two",
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "other" }]
            });

        var sessions = store.List("/tmp/one");

        Assert.Equal(["newer", "older"], sessions.Select(session => session.Id));
        Assert.Equal("first request", sessions[1].Preview);
        Assert.True(sessions[1].PlanMode);
        Assert.Equal(2, sessions[1].UserTurns);
    }

    [Fact]
    public void List_SkipsEmptyAndMalformedDocuments()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(new SessionDocument { Id = "empty", Workspace = "/tmp/one" });
        home.Home.EnsureCreated();
        File.WriteAllText(Path.Combine(home.Home.SessionsDirectory, "bad.json"), "{");

        Assert.Empty(store.List());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    public void TryLoad_RejectsPathTraversal(string id)
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);

        Assert.False(store.TryLoad(id, out _));
    }
}
