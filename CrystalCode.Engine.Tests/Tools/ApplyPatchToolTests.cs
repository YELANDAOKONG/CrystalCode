using System.Text;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class ApplyPatchToolTests
{
    [Fact]
    public async Task InvokeAsync_AddsUpdatesAndDeletesInOneCall()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "keep.txt"), "one\ntwo\nthree\n");
        File.WriteAllText(Path.Combine(root.Path, "drop.txt"), "bye\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Add File: sub/new.txt
            +hello
            +world
            *** Update File: keep.txt
            @@
             one
            -two
            +TWO
             three
            *** Delete File: drop.txt
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("hello\nworld\n", File.ReadAllText(Path.Combine(root.Path, "sub", "new.txt")));
        Assert.Equal("one\nTWO\nthree\n", File.ReadAllText(Path.Combine(root.Path, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(root.Path, "drop.txt")));
        Assert.StartsWith("Applied patch:\nA sub/new.txt\nM keep.txt\nD drop.txt", output.Text, StringComparison.Ordinal);
        Assert.Contains("keep.txt:\n     1|one\n     2|TWO\n     3|three", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_ChecksEverySectionBeforeWritingAny()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "alpha\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Add File: created.txt
            +x
            *** Update File: a.txt
            @@
            -missing line
            +other
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("a.txt", output.Text, StringComparison.Ordinal);
        Assert.Contains("missing line", output.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.Path, "created.txt")));
        Assert.Equal("alpha\n", File.ReadAllText(Path.Combine(root.Path, "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_UsesAtLineToFindTheRightPlace()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(
            Path.Combine(root.Path, "app.py"),
            "def a():\n    return 1\n\ndef b():\n    return 1\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: app.py
            @@ def b():
            -    return 1
            +    return 2
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            "def a():\n    return 1\n\ndef b():\n    return 2\n",
            File.ReadAllText(Path.Combine(root.Path, "app.py")));
    }

    [Fact]
    public async Task InvokeAsync_MatchesDespiteTrailingSpacesAndCurlyQuotes()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "say \u201chi\u201d   \nnext\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: a.txt
            @@
            -say "hi"
            +say "bye"
             next
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("say \"bye\"\nnext\n", File.ReadAllText(Path.Combine(root.Path, "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_KeepsCrlfAndByteOrderMark()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "win.txt");
        File.WriteAllBytes(path, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("a\r\nb\r\nc\r\n")).ToArray());

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: win.txt
            @@
             a
            -b
            +B
             c
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("a\r\nB\r\nc\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task InvokeAsync_KeepsAMissingFinalNewlineMissing()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(path, "one\ntwo");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: a.txt
            @@
             one
            -two
            +2
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("one\n2", File.ReadAllText(path));
    }

    [Fact]
    public async Task InvokeAsync_AppendsWhenABlockOnlyAddsLines()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(path, "one\ntwo\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: a.txt
            @@
            +three
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("one\ntwo\nthree\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task InvokeAsync_EndOfFileMarkerPrefersTheLastMatch()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(path, "end\nmiddle\nend\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: a.txt
            @@
            -end
            +END
            *** End of File
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("end\nmiddle\nEND\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task InvokeAsync_MovesAFileAndAppliesTheBlocks()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "old.txt"), "a\nb\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: old.txt
            *** Move to: moved/new.txt
            @@
            -b
            +B
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.False(File.Exists(Path.Combine(root.Path, "old.txt")));
        Assert.Equal("a\nB\n", File.ReadAllText(Path.Combine(root.Path, "moved", "new.txt")));
        Assert.Contains("R old.txt -> moved/new.txt", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_MoveRefusesAnExistingDestination()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "old.txt"), "a\n");
        File.WriteAllText(Path.Combine(root.Path, "taken.txt"), "keep\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: old.txt
            *** Move to: taken.txt
            @@
            -a
            +b
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("destination already exists", output.Text, StringComparison.Ordinal);
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(root.Path, "old.txt")));
        Assert.Equal("keep\n", File.ReadAllText(Path.Combine(root.Path, "taken.txt")));
    }

    [Fact]
    public async Task InvokeAsync_RefusesToAddAnExistingFile()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "keep\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Add File: a.txt
            +new
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("already exists", output.Text, StringComparison.Ordinal);
        Assert.Equal("keep\n", File.ReadAllText(Path.Combine(root.Path, "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_RefusesTheSamePathInTwoSections()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "x\ny\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: a.txt
            @@
            -x
            +X
            *** Update File: ./a.txt
            @@
            -y
            +Y
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("more than one section", output.Text, StringComparison.Ordinal);
        Assert.Equal("x\ny\n", File.ReadAllText(Path.Combine(root.Path, "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_RefusesCredentialPaths()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, ".ssh"));
        var key = Path.Combine(root.Path, ".ssh", "id_rsa");
        File.WriteAllText(key, "SECRET\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Delete File: .ssh/id_rsa
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Changing credential paths is not allowed.", output.Text);
        Assert.True(File.Exists(key));
    }

    [Fact]
    public async Task InvokeAsync_RefusesMissingFilesForUpdateAndDelete()
    {
        using var root = new TemporaryWorkspace();

        var update = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: nope.txt
            @@
            -a
            +b
            *** End Patch
            """);
        var delete = await Run(
            root,
            """
            *** Begin Patch
            *** Delete File: nope.txt
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, update.Status);
        Assert.Equal(ToolResultStatus.Failure, delete.Status);
    }

    [Fact]
    public async Task InvokeAsync_RefusesBinaryFiles()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "bin.dat");
        File.WriteAllBytes(path, [1, 0, 2, 3]);

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: bin.dat
            @@
            -a
            +b
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("binary", output.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*** Begin Patch\n*** End Patch")]
    [InlineData("*** Update File: a.txt\n@@\n-a\n+b")]
    [InlineData("*** Begin Patch\n*** Update File: a.txt\n@@\n-a\n+b")]
    [InlineData("*** Begin Patch\n*** Add File: a.txt\nnot plus\n*** End Patch")]
    [InlineData("*** Begin Patch\n*** Rename File: a.txt\n*** End Patch")]
    [InlineData("*** Begin Patch\n*** Update File: a.txt\n*** End Patch")]
    public async Task InvokeAsync_RejectsMalformedPatches(string patch)
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "a\n");

        var output = await RunText(root, patch);

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(root.Path, "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_RejectsArgumentsWithoutPatchText()
    {
        using var root = new TemporaryWorkspace();
        var tool = new ApplyPatchTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(JsonToolCall("1", ApplyPatchTool.ToolName, "{}"));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Arguments must include a patchText string.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_AcceptsCrlfPatchText()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(path, "a\nb\n");

        var output = await RunText(
            root,
            "*** Begin Patch\r\n*** Update File: a.txt\r\n@@\r\n-a\r\n+A\r\n b\r\n*** End Patch\r\n");

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("A\nb\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task InvokeAsync_ApplyingTwoBlocksKeepsFileOrder()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(path, "1\n2\n3\n4\n5\n6\n");

        var output = await Run(
            root,
            """
            *** Begin Patch
            *** Update File: a.txt
            @@
            -2
            +two
            @@
            -5
            +five
            +five-b
            *** End Patch
            """);

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("1\ntwo\n3\n4\nfive\nfive-b\n6\n", File.ReadAllText(path));
    }

    [Fact]
    public void Preview_ListsOnlyChangedLinesPerFile()
    {
        var arguments = Arguments(
            """
            *** Begin Patch
            *** Add File: n.txt
            +x
            *** Update File: a.txt
            @@
             keep
            -old
            +new
            *** Delete File: d.txt
            *** End Patch
            """);

        var preview = ApplyPatchTool.Preview(arguments);

        Assert.Equal(["add", "update", "delete"], preview.Select(file => file.Action));
        Assert.Equal(["x"], preview[0].AddedLines);
        Assert.Equal(["old"], preview[1].RemovedLines);
        Assert.Equal(["new"], preview[1].AddedLines);
        Assert.Empty(preview[2].RemovedLines);
    }

    [Fact]
    public void PathsOf_IncludesMoveDestinations()
    {
        var arguments = Arguments(
            """
            *** Begin Patch
            *** Update File: a.txt
            *** Move to: b.txt
            @@
            -x
            +y
            *** Delete File: c.txt
            *** End Patch
            """);

        Assert.Equal(["a.txt", "b.txt", "c.txt"], ApplyPatchTool.PathsOf(arguments));
    }

    [Fact]
    public void PathsOf_IsEmptyForAnUnreadablePatch()
    {
        Assert.Empty(ApplyPatchTool.PathsOf("""{"patchText":"nonsense"}"""));
        Assert.Empty(ApplyPatchTool.PathsOf("not json"));
    }

    [Fact]
    public void Definition_DoesNotNameAnyProvider()
    {
        using var root = new TemporaryWorkspace();
        var tool = new ApplyPatchTool(new Workspace(root.Path));

        Assert.DoesNotContain("openai", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("apply_patch", tool.Definition.Name);
    }

    private static Task<ToolOutput> Run(TemporaryWorkspace root, string patch) =>
        RunText(root, patch);

    private static async Task<ToolOutput> RunText(TemporaryWorkspace root, string patch)
    {
        var tool = new ApplyPatchTool(new Workspace(root.Path));
        var output = await tool.InvokeAsync(
            JsonToolCall("1", ApplyPatchTool.ToolName, Arguments(patch)));
        return output;
    }

    private static string Arguments(string patch) =>
        System.Text.Json.JsonSerializer.Serialize(new { patchText = patch });
}
