using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class WorkspaceAccessStoreTests
{
    [Fact]
    public void TryList_IsEmptyWhenTheFileIsMissing()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var store = new WorkspaceAccessStore(home.Home);

        Assert.True(store.TryList(workspace.Path, out var rules, out var error), error);
        Assert.Empty(rules);
        Assert.False(File.Exists(home.Home.AccessPath));
    }

    [Fact]
    public void TrySet_ReplacesModeAndRemovesTheFileWhenEmpty()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        using var extra = new TemporaryWorkspace();
        var store = new WorkspaceAccessStore(home.Home);
        var path = new Workspace(extra.Path).Root;

        Assert.True(store.TrySet(workspace.Path, new DirectoryAccess(path, "read", path), out var error), error);
        Assert.True(store.TrySet(workspace.Path, new DirectoryAccess(extra.Path, "write"), out error), error);

        Assert.True(store.TryList(workspace.Path, out var rules, out error), error);
        var rule = Assert.Single(rules);
        Assert.Equal(path, rule.Path);
        Assert.Equal("write", rule.Mode);
        Assert.Null(rule.SourcePath);
        Assert.DoesNotContain("sourcePath", File.ReadAllText(home.Home.AccessPath), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(home.Home.AccessPath));
        }

        Assert.True(store.TryRemove(workspace.Path, path, path, out var removed, out error), error);
        Assert.True(removed);
        Assert.False(File.Exists(home.Home.AccessPath));
    }

    [Fact]
    public void TrySet_KeepsADistinctEntryPathAndIgnoresChildWorkspaces()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        using var extra = new TemporaryWorkspace();
        var child = Directory.CreateDirectory(Path.Combine(workspace.Path, "src")).FullName;
        var store = new WorkspaceAccessStore(home.Home);
        var target = new Workspace(extra.Path).Root;
        var link = Path.Combine(workspace.Path, "link");

        Assert.True(
            store.TrySet(workspace.Path, new DirectoryAccess(target, "read", link), out var error),
            error);

        Assert.True(store.TryList(workspace.Path, out var rules, out error), error);
        Assert.Equal(link, Assert.Single(rules).SourcePath);
        Assert.Contains("\"sourcePath\"", File.ReadAllText(home.Home.AccessPath), StringComparison.Ordinal);
        Assert.True(store.TryList(child, out var childRules, out error), error);
        Assert.Empty(childRules);
        Assert.True(store.TryRemove(workspace.Path, link, null, out var removed, out error), error);
        Assert.True(removed);
    }

    [Fact]
    public void TrySet_RejectsACredentialDirectory()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var credentials = Directory.CreateDirectory(Path.Combine(workspace.Path, ".ssh")).FullName;
        var store = new WorkspaceAccessStore(home.Home);

        Assert.False(store.TrySet(workspace.Path, new DirectoryAccess(credentials, "read"), out var error));
        Assert.Equal("Credential directories cannot be granted access.", error);
        Assert.False(File.Exists(home.Home.AccessPath));
    }

    [Fact]
    public void TrySeed_SkipsAMissingDirectoryWithoutRewritingTheFile()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        using var extra = new TemporaryWorkspace();
        var store = new WorkspaceAccessStore(home.Home);
        var root = new Workspace(workspace.Path).Root;
        var present = new Workspace(extra.Path).Root;
        var missing = Path.Combine(workspace.Path, "gone");
        home.Home.EnsureCreated();
        File.WriteAllText(
            home.Home.AccessPath,
            $$"""
            {
              "workspaces": [
                {
                  "path": {{Json(root)}},
                  "directories": [
                    { "path": {{Json(missing)}}, "mode": "read" },
                    { "path": {{Json(present)}}, "mode": "write" }
                  ]
                }
              ]
            }
            """);
        var before = File.ReadAllText(home.Home.AccessPath);

        Assert.True(store.TrySeed(workspace.Path, out var rules, out var error, out var skipped), error);

        Assert.Equal(present, Assert.Single(rules).Path);
        Assert.Equal("write", rules[0].Mode);
        Assert.Contains(skipped, note => note == "Default directory not found: " + missing + ".");
        Assert.Equal(before, File.ReadAllText(home.Home.AccessPath));
    }

    [Fact]
    public void TrySeed_SkipsACredentialDirectory()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var credentials = Directory.CreateDirectory(Path.Combine(workspace.Path, ".ssh")).FullName;
        var root = new Workspace(workspace.Path).Root;
        home.Home.EnsureCreated();
        File.WriteAllText(
            home.Home.AccessPath,
            $$"""
            {
              "workspaces": [
                {
                  "path": {{Json(root)}},
                  "directories": [
                    { "path": {{Json(credentials)}}, "mode": "read" }
                  ]
                }
              ]
            }
            """);
        var store = new WorkspaceAccessStore(home.Home);

        Assert.True(store.TrySeed(workspace.Path, out var rules, out var error, out var skipped), error);

        Assert.Empty(rules);
        Assert.Contains("Credential directories cannot be granted access.", skipped);
    }

    [Fact]
    public void TrySeed_RejectsConflictingModesWithoutSeeding()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        using var extra = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path).Root;
        var path = new Workspace(extra.Path).Root;
        home.Home.EnsureCreated();
        File.WriteAllText(
            home.Home.AccessPath,
            $$"""
            {
              "workspaces": [
                {
                  "path": {{Json(root)}},
                  "directories": [
                    { "path": {{Json(path)}}, "mode": "read" },
                    { "path": {{Json(path)}}, "mode": "write" }
                  ]
                }
              ]
            }
            """);
        var before = File.ReadAllText(home.Home.AccessPath);
        var store = new WorkspaceAccessStore(home.Home);

        Assert.False(store.TrySeed(workspace.Path, out var rules, out var error, out var skipped));

        Assert.Empty(rules);
        Assert.Empty(skipped);
        Assert.Equal(WorkspaceAccessStore.ConflictError, error);
        Assert.Equal(before, File.ReadAllText(home.Home.AccessPath));
    }

    [Fact]
    public void TrySeed_RejectsACorruptFile()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(home.Home.AccessPath, "{");
        var store = new WorkspaceAccessStore(home.Home);

        Assert.False(store.TryList(workspace.Path, out _, out var error));
        Assert.Equal(WorkspaceAccessStore.ReadError, error);
        Assert.False(store.TrySeed(workspace.Path, out var rules, out error, out _));
        Assert.Empty(rules);
        Assert.Equal(WorkspaceAccessStore.ReadError, error);
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
