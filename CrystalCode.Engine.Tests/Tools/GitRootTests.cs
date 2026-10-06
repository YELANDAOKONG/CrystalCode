using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class GitRootTests
{
    [Fact]
    public void Find_ReturnsTheDirectoryThatContainsGit()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        var child = Path.Combine(root.Path, "src");
        Directory.CreateDirectory(child);

        Assert.Equal(Path.GetFullPath(root.Path), GitRoot.Find(child));
        Assert.Equal(Path.GetFullPath(root.Path), GitRoot.TrustRoot(child));
    }

    [Fact]
    public void Find_AcceptsAGitFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, ".git"), "gitdir: ../real.git");

        Assert.Equal(Path.GetFullPath(root.Path), GitRoot.Find(root.Path));
    }

    [Fact]
    public void TrustRoot_UsesTheWorkspaceWhenItIsNotARepository()
    {
        using var root = new TemporaryWorkspace();
        var child = Path.Combine(root.Path, "src");
        Directory.CreateDirectory(child);

        Assert.Null(GitRoot.Find(child));
        Assert.Equal(Path.GetFullPath(child), GitRoot.TrustRoot(child));
        Assert.NotEqual(GitRoot.TrustRoot(root.Path), GitRoot.TrustRoot(child));
    }
}
