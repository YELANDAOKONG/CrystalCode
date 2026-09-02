using CrystalCode.Home;
using CrystalCode.Sessions;
using CrystalCode.Tests.Home;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class SessionResumeTests
{
    [Fact]
    public void TryLoad_ReadsSessionById()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(
            new SessionDocument
            {
                Id = "abcd",
                Workspace = "/tmp/one",
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hi" }]
            });

        Assert.True(
            SessionResume.TryLoad(store, "/tmp/other", "abcd", out var document, out _));
        Assert.Equal("abcd", document.Id);
    }

    [Fact]
    public void TryLoad_WithoutId_UsesLatestForWorkspace()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(
            new SessionDocument
            {
                Id = "old",
                Workspace = "/tmp/one",
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "old" }]
            });
        Thread.Sleep(20);
        store.Save(
            new SessionDocument
            {
                Id = "new",
                Workspace = "/tmp/one",
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "new" }]
            });

        Assert.True(
            SessionResume.TryLoad(store, "/tmp/one", id: null, out var document, out _));
        Assert.Equal("new", document.Id);
    }

    [Fact]
    public void TryLoad_MissingId_ReturnsNotFound()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);

        Assert.False(
            SessionResume.TryLoad(store, "/tmp/one", "missing", out _, out var error));
        Assert.Equal("Session not found  missing", error);
    }

    [Fact]
    public void TryLoad_EmptyItems_ReturnsEmpty()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);
        store.Save(
            new SessionDocument
            {
                Id = "empty",
                Workspace = "/tmp/one"
            });

        Assert.False(
            SessionResume.TryLoad(store, "/tmp/one", "empty", out _, out var error));
        Assert.Equal("Session is empty", error);
    }

    [Fact]
    public void TryLoad_WithoutId_WhenWorkspaceHasNone()
    {
        using var home = new TemporaryHome();
        var store = new SessionStore(home.Home);

        Assert.False(
            SessionResume.TryLoad(store, "/tmp/one", id: null, out _, out var error));
        Assert.Equal("No session for this workspace", error);
    }
}
