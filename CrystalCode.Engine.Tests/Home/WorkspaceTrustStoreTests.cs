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
