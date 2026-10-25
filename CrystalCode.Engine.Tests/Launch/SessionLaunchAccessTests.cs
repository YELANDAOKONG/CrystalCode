using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Launch;

public sealed class SessionLaunchAccessTests
{
    [Fact]
    public void TryRead_RepeatedPathsAreCanonicalAndModesAreDistinct()
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reference"));
            Directory.CreateDirectory(Path.Combine(root, "output"));
            var settings = new SessionLaunchOptions
            {
                AllowRead = ["reference", "./reference"], AllowWrite = ["output"]
            };

            Assert.True(SessionLaunchAccess.TryRead(settings, root, out var rules, out var error), error);
            Assert.Equal(2, rules.Count);
            var reference = Assert.Single(rules, rule => rule.Mode == "read");
            var output = Assert.Single(rules, rule => rule.Mode == "write");
            Assert.Equal(new Workspace(Path.Combine(root, "reference")).Root, reference.Path);
            Assert.Equal(new Workspace(Path.Combine(root, "output")).Root, output.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryRead_SymlinkRetainsEntryPathAlongsideCanonicalTarget()
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var link = Path.Combine(root, "reference-link");
            Directory.CreateSymbolicLink(link, target);

            Assert.True(SessionLaunchAccess.TryRead(
                new SessionLaunchOptions { AllowRead = ["reference-link"] }, root,
                out var rules, out var error), error);

            var rule = Assert.Single(rules);
            Assert.Equal(new Workspace(target).Root, rule.Path);
            Assert.Equal(Path.GetFullPath(link), rule.SourcePath);
            Assert.Equal("read", rule.Mode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryRead_EntryPathStaysLexicalWhenAnAncestorIsALink()
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            var privateVar = Directory.CreateDirectory(Path.Combine(root, "private", "var")).FullName;
            var ancestor = Path.Combine(root, "var");
            Directory.CreateSymbolicLink(ancestor, privateVar);
            var workspace = Path.Combine(ancestor, "workspace");
            var target = Path.Combine(workspace, "target");
            Directory.CreateDirectory(target);
            var link = Path.Combine(workspace, "reference-link");
            Directory.CreateSymbolicLink(link, target);

            Assert.True(SessionLaunchAccess.TryRead(
                new SessionLaunchOptions { AllowRead = ["reference-link"] }, workspace,
                out var rules, out var error), error);

            var rule = Assert.Single(rules);
            Assert.Equal(new Workspace(target).Root, rule.Path);
            Assert.Equal(Path.GetFullPath(link), rule.SourcePath);
            Assert.NotEqual(rule.Path, rule.SourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryRead_ConflictingModesAreRejected()
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            var settings = new SessionLaunchOptions { AllowRead = ["."], AllowWrite = [root] };
            Assert.False(SessionLaunchAccess.TryRead(settings, root, out var rules, out var error));
            Assert.Empty(rules);
            Assert.Contains("both --allow-read and --allow-write", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData(".ssh")]
    public void TryRead_MissingOrCredentialDirectoryIsRejected(string name)
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".ssh"));
            Assert.False(SessionLaunchAccess.TryRead(new SessionLaunchOptions { AllowRead = [name] }, root, out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
