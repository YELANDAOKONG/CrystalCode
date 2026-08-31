using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

public sealed class WorkspaceTests
{
    [Fact]
    public void TryResolveReadableFile_AcceptsPathOutsideRoot()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var file = Path.Combine(outside.Path, "note.txt");
        File.WriteAllText(file, "hello");
        var workspace = new Workspace(root.Path);

        var found = workspace.TryResolveReadableFile(file, out var fullPath, out var error);

        Assert.True(found);
        Assert.Equal(string.Empty, error);
        Assert.Equal(Path.GetFullPath(file), fullPath);
    }

    [Fact]
    public void TryResolveExistingFile_StillRejectsPathOutsideRoot()
    {
        using var root = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        File.WriteAllText(Path.Combine(root.Path, "inside.txt"), "ok");

        var escaped = workspace.TryResolveExistingFile(
            Path.Combine("..", "outside.txt"),
            out _,
            out var error);

        Assert.False(escaped);
        Assert.Equal("Path is outside the workspace.", error);
    }

    [Fact]
    public void TryResolveExistingFile_AcceptsRelativeFileInsideRoot()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "hello");
        var workspace = new Workspace(root.Path);

        var found = workspace.TryResolveExistingFile("note.txt", out var fullPath, out var error);

        Assert.True(found);
        Assert.Equal(string.Empty, error);
        Assert.Equal("note.txt", workspace.ToRelative(fullPath));
    }

    [Fact]
    public void TryResolveExistingLocation_AcceptsDirectoryInsideRoot()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, "src"));
        var workspace = new Workspace(root.Path);

        var found = workspace.TryResolveExistingLocation("src", out var fullPath, out var error);

        Assert.True(found);
        Assert.Equal(string.Empty, error);
        Assert.Equal("src", workspace.ToRelative(fullPath));
    }
}
