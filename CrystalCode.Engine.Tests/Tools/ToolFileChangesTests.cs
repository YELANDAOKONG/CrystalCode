using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class ToolFileChangesTests
{
    [Fact]
    public void Edit_PreviewsTheReplacementFromDisk()
    {
        using var workspace = new TemporaryWorkspace();
        var path = System.IO.Path.Combine(workspace.Path, "a.txt");
        File.WriteAllText(path, "one two two\n");

        var once = ToolFileChanges.Preview(workspace.Path, Call("edit", """{"path":"a.txt","old_string":"two","new_string":"three"}"""));
        var all = ToolFileChanges.Preview(workspace.Path, Call("edit", """{"path":"a.txt","old_string":"two","new_string":"three","replace_all":true}"""));

        var change = Assert.Single(once);
        Assert.Equal(Root(workspace, "a.txt"), change.Path);
        Assert.Equal("one two two\n", change.OldText);
        Assert.Equal("one three two\n", change.NewText);
        Assert.Equal("one three three\n", Assert.Single(all).NewText);
        Assert.Equal("one two two\n", File.ReadAllText(path));
    }

    [Fact]
    public void Write_ToANewFileHasNoOldText()
    {
        using var workspace = new TemporaryWorkspace();

        var change = Assert.Single(ToolFileChanges.Preview(workspace.Path, Call("write", """{"path":"new.txt","contents":"hello"}""")));

        Assert.Null(change.OldText);
        Assert.Equal("hello", change.NewText);
        Assert.False(File.Exists(change.Path));
    }

    [Fact]
    public void Patch_CoversAddDeleteAndMove()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "old.txt"), "gone\n");
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "from.txt"), "keep\n");
        var patch = System.Text.Json.JsonSerializer.Serialize(new
        {
            patchText = "*** Begin Patch\n*** Add File: added.txt\n+new\n*** Delete File: old.txt\n"
                + "*** Update File: from.txt\n*** Move to: to.txt\n@@\n-keep\n+kept\n*** End Patch"
        });

        var changes = ToolFileChanges.Preview(workspace.Path, Call("apply_patch", patch));

        Assert.Equal(3, changes.Count);
        Assert.Equal("new\n", changes[0].NewText);
        Assert.True(changes[1].Delete);
        Assert.Equal("gone\n", changes[1].OldText);
        Assert.EndsWith("to.txt", changes[2].Path, StringComparison.Ordinal);
        Assert.EndsWith("from.txt", changes[2].MoveFrom, StringComparison.Ordinal);
        Assert.Equal("kept\n", changes[2].NewText);
    }

    [Fact]
    public void CredentialAndBinaryFiles_AreLeftOut()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(System.IO.Path.Combine(workspace.Path, ".ssh"));
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, ".ssh", "id_test"), "private\n");
        File.WriteAllBytes(System.IO.Path.Combine(workspace.Path, "blob.bin"), [0, 1, 2, 0, 3]);

        Assert.Empty(ToolFileChanges.Preview(workspace.Path, Call("write", """{"path":".ssh/id_test","contents":"x"}""")));
        Assert.Empty(ToolFileChanges.Preview(workspace.Path, Call("write", """{"path":"blob.bin","contents":"x"}""")));
        Assert.Empty(ToolFileChanges.Preview(workspace.Path, Call("edit", """{"path":"missing.txt","old_string":"a","new_string":"b"}""")));
    }

    [Fact]
    public void Paths_ResolveAgainstTheWorkspace()
    {
        using var workspace = new TemporaryWorkspace();

        var paths = ToolFileChanges.Paths(workspace.Path, Call("read", """{"path":"src/a.cs"}"""));

        Assert.Equal(Root(workspace, System.IO.Path.Combine("src", "a.cs")), Assert.Single(paths));
        Assert.Empty(ToolFileChanges.Paths(workspace.Path, Call("bash", """{"command":"ls"}""")));
    }

    [Fact]
    public void Summary_FlattensLineBreaks()
    {
        Assert.Equal("Bash  echo a echo b", ToolCallSummary.Text("bash", """{"command":"echo a\necho b"}"""));
    }

    /// <summary>The workspace resolves its root, so a temp path under /var reads /private/var on macOS.</summary>
    private static string Root(TemporaryWorkspace workspace, string relative) =>
        System.IO.Path.Combine(new Workspace(workspace.Path).Root, relative);

    private static ToolCall Call(string name, string arguments) => new("call", name, arguments, ToolInputKind.Json);
}
