using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class WorkspaceTrustStoreTests
{
    [Fact]
    public void Contains_IsFalseWhenTheFileIsMissing()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var store = new WorkspaceTrustStore(home.Home);

        Assert.False(store.Contains(workspace.Path));
        Assert.False(File.Exists(home.Home.TrustedPath));
    }

    [Fact]
    public void Remember_IsIdempotentAndCoversARepositoryChild()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var child = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(child);
        var store = new WorkspaceTrustStore(home.Home);

        store.Remember(child);
        store.Remember(workspace.Path);

        Assert.True(store.Contains(child));
        Assert.True(store.Contains(workspace.Path));
        var json = File.ReadAllText(home.Home.TrustedPath);
        Assert.Equal(1, Count(json, GitRoot.TrustRoot(workspace.Path)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(home.Home.TrustedPath));
        }
    }

    [Fact]
    public void Contains_TrustsTheOperatorSpaceWithoutALedgerEntry()
    {
        using var home = new TemporaryHome();
        Directory.CreateDirectory(Path.Combine(home.Root, ".git"));
        var space = OperatorSpace.EnsureCreated(home.Home);
        var store = new WorkspaceTrustStore(home.Home);

        Assert.True(store.Contains(space));
        Assert.Equal(space, store.TrustRoot(space));
        Assert.False(store.Contains(home.Root));
        Assert.False(File.Exists(home.Home.TrustedPath));

        store.Remember(space);
        store.Forget(space);

        Assert.False(File.Exists(home.Home.TrustedPath));
        Assert.True(store.Contains(space));
    }

    [Fact]
    public void Forget_OperatorSpaceLeavesASeparateParentGrant()
    {
        using var home = new TemporaryHome();
        Directory.CreateDirectory(Path.Combine(home.Root, ".git"));
        var space = OperatorSpace.EnsureCreated(home.Home);
        var store = new WorkspaceTrustStore(home.Home);
        store.Remember(home.Root);

        store.Forget(space);

        Assert.True(store.Contains(home.Root));
        Assert.True(store.Contains(space));
        var json = File.ReadAllText(home.Home.TrustedPath);
        Assert.Contains(new Workspace(home.Root).Root, json, StringComparison.Ordinal);
        Assert.DoesNotContain(space, json, StringComparison.Ordinal);
    }

    [Fact]
    public void Contains_TrustsAChildWhenTheOperatorSpaceIsTheGitRoot()
    {
        using var home = new TemporaryHome();
        var space = OperatorSpace.EnsureCreated(home.Home);
        Directory.CreateDirectory(Path.Combine(space, ".git"));
        var child = Path.Combine(space, "src");
        Directory.CreateDirectory(child);
        var store = new WorkspaceTrustStore(home.Home);

        Assert.Equal(space, store.TrustRoot(child));
        Assert.True(store.Contains(child));
        store.Remember(child);
        Assert.False(File.Exists(home.Home.TrustedPath));
    }

    [Fact]
    public void Contains_DoesNotTrustAChildOfTheOperatorSpace()
    {
        using var home = new TemporaryHome();
        var space = OperatorSpace.EnsureCreated(home.Home);
        var child = Path.Combine(space, "notes");
        Directory.CreateDirectory(child);
        var store = new WorkspaceTrustStore(home.Home);

        Assert.Equal(new Workspace(child).Root, store.TrustRoot(child));
        Assert.False(store.Contains(child));
    }

    [Fact]
    public void Forget_RemovesOnlyThatTrustRoot()
    {
        using var home = new TemporaryHome();
        using var first = new TemporaryWorkspace();
        using var second = new TemporaryWorkspace();
        var store = new WorkspaceTrustStore(home.Home);
        store.Remember(first.Path);
        store.Remember(second.Path);

        store.Forget(first.Path);
        store.Forget(first.Path);

        Assert.False(store.Contains(first.Path));
        Assert.True(store.Contains(second.Path));
    }

    private static int Count(string json, string path) =>
        json.Split(path, StringSplitOptions.None).Length - 1;
}
