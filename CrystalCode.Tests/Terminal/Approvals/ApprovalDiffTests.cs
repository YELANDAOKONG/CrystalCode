using Crystal.Tools;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

using Xunit;
using CrystalCode.Terminal.Approvals;

namespace CrystalCode.Tests.Terminal.Approvals;

public sealed class ApprovalDiffTests
{
    [Fact]
    public void Lines_EditShowsRemovedAndAdded()
    {
        var call = JsonToolCall(
            "1",
            EditTool.ToolName,
            """{"path":"src/App.cs","old_string":"foo","new_string":"bar"}""");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal(
            [
                (Theme.DiffRemoved, "-foo"),
                (Theme.DiffAdded, "+bar")
            ],
            lines);
    }

    [Fact]
    public void Lines_EditWithReplaceAllSaysEveryOccurrenceChanges()
    {
        var call = JsonToolCall(
            "1",
            EditTool.ToolName,
            """{"path":"src/App.cs","old_string":"foo","new_string":"bar","replace_all":true}""");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal(
            [
                (Theme.Muted, ApprovalDiff.ReplaceAllNotice),
                (Theme.DiffRemoved, "-foo"),
                (Theme.DiffAdded, "+bar")
            ],
            lines);
    }

    [Fact]
    public void Lines_EditWithReplaceAllFalseShowsNoNotice()
    {
        var call = JsonToolCall(
            "1",
            EditTool.ToolName,
            """{"path":"src/App.cs","old_string":"foo","new_string":"bar","replace_all":false}""");

        Assert.DoesNotContain(ApprovalDiff.Lines(call), line => line.Text == ApprovalDiff.ReplaceAllNotice);
    }

    [Fact]
    public void Lines_PatchHeadsEachFileAndListsOnlyChangedLines()
    {
        var call = PatchCall(
            "*** Add File: n.txt\n+hello\n"
            + "*** Update File: a.txt\n@@\n keep\n-old\n+new\n"
            + "*** Update File: b.txt\n*** Move to: c.txt\n@@\n-x\n+y\n"
            + "*** Delete File: d.txt");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal(
            [
                (Theme.Accent, "Add n.txt"),
                (Theme.DiffAdded, "+hello"),
                (Theme.Accent, "Update a.txt"),
                (Theme.DiffRemoved, "-old"),
                (Theme.DiffAdded, "+new"),
                (Theme.Accent, "Move b.txt to c.txt"),
                (Theme.DiffRemoved, "-x"),
                (Theme.DiffAdded, "+y"),
                (Theme.Accent, "Delete d.txt")
            ],
            lines);
    }

    [Fact]
    public void Lines_PatchCapsTheWholeCardNotEachFile()
    {
        var body = string.Join('\n', Enumerable.Range(1, 40).Select(n => "+line" + n));
        var call = PatchCall($"*** Add File: a.txt\n{body}\n*** Add File: b.txt\n+tail");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal(ApprovalDiff.MaximumLines + 1, lines.Count);
        Assert.Equal((Theme.Accent, "Add a.txt"), lines[0]);
        Assert.Equal(Theme.Muted, lines[^1].Color);
        Assert.StartsWith("... (", lines[^1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.Text == "Add b.txt");
    }

    [Fact]
    public void Lines_PatchSpellsOutControlCharacters()
    {
        var call = PatchCall("*** Add File: a.txt\n+safe\u001b[2Kline");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal((Theme.DiffAdded, "+safe\\x1b[2Kline"), lines[1]);
    }

    [Fact]
    public void Lines_UnreadablePatchShowsNothing()
    {
        var call = JsonToolCall("1", ApplyPatchTool.ToolName, """{"patchText":"nonsense"}""");

        Assert.Empty(ApprovalDiff.Lines(call));
    }

    private static ToolCall PatchCall(string sections) =>
        JsonToolCall(
            "1",
            ApplyPatchTool.ToolName,
            System.Text.Json.JsonSerializer.Serialize(
                new { patchText = "*** Begin Patch\n" + sections + "\n*** End Patch" }));

    [Fact]
    public void Lines_WriteShowsAddedContents()
    {
        var call = JsonToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"hello"}""");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal([(Theme.DiffAdded, "+hello")], lines);
    }

    [Fact]
    public void Lines_CapsLongBodies()
    {
        var body = string.Join('\n', Enumerable.Range(1, ApprovalDiff.MaximumLines + 1)
            .Select(index => "line" + index));
        var call = JsonToolCall(
            "1",
            WriteTool.ToolName,
            "{\"path\":\"src/App.cs\",\"contents\":\"" + body.Replace("\n", "\\n", StringComparison.Ordinal) + "\"}");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal(ApprovalDiff.MaximumLines + 1, lines.Count);
        Assert.Equal((Theme.DiffAdded, "+line1"), lines[0]);
        Assert.Equal(
            (Theme.Muted, "... (1 more lines)"),
            lines[^1]);
    }

    [Fact]
    public void Lines_OtherToolsEmpty()
    {
        var call = JsonToolCall("1", "bash", """{"command":"ls"}""");

        Assert.Empty(ApprovalDiff.Lines(call));
    }

    [Fact]
    public void Lines_SpellOutControlCharactersInPreviewText()
    {
        var call = JsonToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"x","contents":"safe\u001b[2Kline"}""");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal([(Theme.DiffAdded, "+safe\\x1b[2Kline")], lines);
    }
}
