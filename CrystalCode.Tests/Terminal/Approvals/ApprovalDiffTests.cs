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
        var call = new ToolCall(
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
    public void Lines_WriteShowsAddedContents()
    {
        var call = new ToolCall(
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
        var call = new ToolCall(
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
        var call = new ToolCall("1", "bash", """{"command":"ls"}""");

        Assert.Empty(ApprovalDiff.Lines(call));
    }

    [Fact]
    public void Lines_SpellOutControlCharactersInPreviewText()
    {
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"x","contents":"safe\u001b[2Kline"}""");

        var lines = ApprovalDiff.Lines(call);

        Assert.Equal([(Theme.DiffAdded, "+safe\\x1b[2Kline")], lines);
    }
}
