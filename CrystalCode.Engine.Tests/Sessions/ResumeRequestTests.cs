using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ResumeRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_BlankArgument_UsesTheCurrentWorkspace(string? argument)
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        var store = new SessionStore(home.Home);

        Assert.True(ResumeRequest.TryParse(argument, root.Path, store, out var request, out var error));

        Assert.Equal(string.Empty, error);
        Assert.Equal(ResumeRequest.Kind.CurrentWorkspace, request.Target);
        Assert.Null(request.Value);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    public void TryParse_All_ListsEveryWorkspace(string argument)
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "all"));
        var store = new SessionStore(home.Home);
        store.Save(Message("all", root.Path, "reserved"));

        Assert.True(ResumeRequest.TryParse(argument, root.Path, store, out var request, out _));

        Assert.Equal(ResumeRequest.Kind.AllWorkspaces, request.Target);
        Assert.Null(request.Value);
    }

    [Fact]
    public void TryParse_RelativeDirectory_NamesThatWorkspace()
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        var child = Path.Combine(root.Path, "child");
        Directory.CreateDirectory(child);
        var store = new SessionStore(home.Home);

        Assert.True(ResumeRequest.TryParse("child", root.Path, store, out var request, out _));
        Assert.True(ResumeRequest.TryParse(".", root.Path, store, out var current, out _));

        Assert.Equal(ResumeRequest.Kind.Workspace, request.Target);
        Assert.Equal(new Workspace(child).Root, request.Value);
        Assert.Equal(new Workspace(root.Path).Root, current.Value);
    }

    [Fact]
    public void TryParse_ExistingAllDirectory_WithDotSlash_IsAWorkspace()
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        var all = Path.Combine(root.Path, "all");
        Directory.CreateDirectory(all);
        var store = new SessionStore(home.Home);

        Assert.True(ResumeRequest.TryParse("./all", root.Path, store, out var request, out _));

        Assert.Equal(ResumeRequest.Kind.Workspace, request.Target);
        Assert.Equal(new Workspace(all).Root, request.Value);
    }

    [Fact]
    public void TryParse_MissingPath_ReturnsDirectoryNotFound()
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        var store = new SessionStore(home.Home);
        var missing = Path.Combine(root.Path, "missing");

        Assert.False(ResumeRequest.TryParse(missing, root.Path, store, out _, out var error));

        Assert.Equal("Directory not found.", error);
    }

    [Fact]
    public void TryParse_UnknownToken_IsASessionId()
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        var store = new SessionStore(home.Home);

        Assert.True(ResumeRequest.TryParse("deadbeef", root.Path, store, out var request, out _));

        Assert.Equal(ResumeRequest.Kind.Session, request.Target);
        Assert.Equal("deadbeef", request.Value);
    }

    [Fact]
    public void TryParse_DirectoryAndSession_IsAmbiguous()
    {
        using var home = new TemporaryHome();
        using var root = new TemporaryWorkspace();
        const string id = "abcd";
        Directory.CreateDirectory(Path.Combine(root.Path, id));
        var store = new SessionStore(home.Home);
        store.Save(Message(id, root.Path, "both"));

        Assert.False(ResumeRequest.TryParse(id, root.Path, store, out _, out var error));

        Assert.Equal("Resume target matches both a session and a directory.", error);
    }

    [Fact]
    public void SameDirectory_MatchesCanonicalPaths()
    {
        using var root = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();

        Assert.True(ResumeRequest.SameDirectory(root.Path, root.Path + Path.DirectorySeparatorChar));
        Assert.False(ResumeRequest.SameDirectory(root.Path, other.Path));
    }

    [Fact]
    public void TryCanonicalWorkspace_RequiresAnExistingDirectory()
    {
        using var root = new TemporaryWorkspace();
        var missing = Path.Combine(root.Path, "gone");

        Assert.True(ResumeRequest.TryCanonicalWorkspace(root.Path, out var canonical, out _));
        Assert.False(ResumeRequest.TryCanonicalWorkspace(missing, out _, out var error));

        Assert.Equal(new Workspace(root.Path).Root, canonical);
        Assert.Equal("Session workspace is not a directory.", error);
    }

    private static SessionDocument Message(string id, string workspace, string text) =>
        new()
        {
            Id = id,
            Workspace = workspace,
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = text }]
        };
}
