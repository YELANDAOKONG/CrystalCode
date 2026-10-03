using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

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
        Assert.Equal(Workspace.Canonicalize(file), fullPath);
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

    [Fact]
    public void Canonicalize_ResolvesALinkWhoseTargetUsesAnAncestorLink()
    {
        using var root = new TemporaryWorkspace();
        var privateFolders = Path.Combine(root.Path, "private", "var", "folders");
        Directory.CreateDirectory(privateFolders);
        var ancestor = Path.Combine(root.Path, "var");
        Directory.CreateSymbolicLink(ancestor, Path.Combine(root.Path, "private", "var"));
        var real = Path.Combine(ancestor, "folders", "real-skills");
        Directory.CreateDirectory(real);
        var file = Path.Combine(real, "notes.md");
        File.WriteAllText(file, "extra");
        var link = Path.Combine(ancestor, "folders", "linked-skills");
        Directory.CreateSymbolicLink(link, real);
        var noteLink = Path.Combine(ancestor, "folders", "note-link.txt");
        File.CreateSymbolicLink(noteLink, file);

        var canonicalReal = Workspace.Canonicalize(
            Path.Combine(privateFolders, "real-skills"));
        var canonicalFile = Workspace.Canonicalize(Path.Combine(privateFolders, "real-skills", "notes.md"));
        Assert.Equal(canonicalReal, Workspace.Canonicalize(real));
        Assert.Equal(canonicalReal, Workspace.Canonicalize(link));
        Assert.Equal(canonicalFile, Workspace.Canonicalize(file));
        Assert.Equal(canonicalFile, Workspace.Canonicalize(Path.Combine(link, "notes.md")));
        Assert.Equal(canonicalFile, Workspace.Canonicalize(noteLink));
    }

    [Fact]
    public void Canonicalize_ResolvesRelativeAndChainedLinks()
    {
        using var root = new TemporaryWorkspace();
        var real = Path.Combine(root.Path, "real-skills");
        Directory.CreateDirectory(real);
        var middle = Path.Combine(root.Path, "middle-skills");
        Directory.CreateSymbolicLink(middle, "real-skills");
        var outer = Path.Combine(root.Path, "outer-skills");
        Directory.CreateSymbolicLink(outer, "middle-skills");

        var canonical = Workspace.Canonicalize(real);
        Assert.Equal(canonical, Workspace.Canonicalize(middle));
        Assert.Equal(canonical, Workspace.Canonicalize(outer));
        Assert.Equal(
            Path.Combine(canonical, "notes.md"),
            Workspace.Canonicalize(Path.Combine(outer, "notes.md")));
    }

    [Fact]
    public void Canonicalize_AppliesParentSegmentAfterResolvingTheLink()
    {
        using var root = new TemporaryWorkspace();
        var elsewhere = Path.Combine(root.Path, "private", "elsewhere");
        var nested = Path.Combine(elsewhere, "nested");
        Directory.CreateDirectory(nested);
        var alias = Path.Combine(root.Path, "alias");
        Directory.CreateSymbolicLink(alias, nested);
        var up = Path.Combine(alias, "up");
        Directory.CreateSymbolicLink(up, "..");

        Assert.Equal(Workspace.Canonicalize(elsewhere), Workspace.Canonicalize(up));
    }
}
