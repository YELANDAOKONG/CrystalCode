using CrystalCode.Display.Composer;
using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Composer;

public sealed class QuickPromptPickerTests
{
    [Fact]
    public void Handle_SearchesBodyAndRequiresExplicitSend()
    {
        var picker = new QuickPromptPicker([
            new("first", "other", null), new("second", "审查代码", null)], "shortcuts", null);
        picker.Handle(new InputPaste("审查"));
        Assert.False(picker.Send);
        picker.Handle(Key(ConsoleKey.Tab));
        picker.Handle(Key(ConsoleKey.Enter));
        Assert.True(picker.Send);
        Assert.Equal("second", picker.Chosen?.Name);
    }

    [Fact]
    public void Handle_DisabledEntryCannotBeAppliedAndEscapeCancels()
    {
        var picker = new QuickPromptPicker([new("broken", "", "Empty")], "shortcuts", null);
        picker.Handle(Key(ConsoleKey.Enter));
        Assert.False(picker.Closed);
        picker.Handle(Key(ConsoleKey.Escape));
        Assert.True(picker.Closed);
        Assert.Null(picker.Chosen);
    }

    [Fact]
    public void Paint_PreviewCanReachLastLineAndResize()
    {
        var body = string.Join('\n', Enumerable.Range(0, 100).Select(index => $"Line {index}"));
        var picker = new QuickPromptPicker([new("long", body, null)], "shortcuts", null);
        for (var index = 0; index < 100; index++)
        {
            picker.Handle(Key(ConsoleKey.PageDown));
        }
        var lines = picker.Paint(60, 20);
        Assert.Contains(lines, line => line.Plain.Contains("Line 99"));
        Assert.True(lines.Count <= 20);
        Assert.True(picker.Paint(30, 15).Count <= 15);
    }

    [Fact]
    public void Handle_DisabledEntryShowsReason()
    {
        var picker = new QuickPromptPicker([new("broken", "", "Empty")], "shortcuts", null);
        picker.Handle(Key(ConsoleKey.Enter));
        Assert.Contains(picker.Paint(60, 20), line => line.Plain.Contains("broken cannot be used: Empty"));
    }

    [Fact]
    public void Paint_EscapesMarkupAndKeepsListOnShortTerminal()
    {
        var picker = new QuickPromptPicker([new("[red]name", "[bold]body[/]", null)], "shortcuts", null);
        var lines = picker.Paint(50, 10);
        Assert.Contains(lines, line => line.Plain.Contains("[red]name"));
        Assert.Contains(lines, line => line.Plain.Contains("[bold]body[/]"));
        Assert.True(lines.Count <= 10);
    }

    [Fact]
    public void Paint_ReportsMatchCount()
    {
        var picker = new QuickPromptPicker([new("alpha", "x", null), new("beta", "y", null)], "shortcuts", null);
        picker.Handle(new InputPaste("alp"));
        Assert.Contains(picker.Paint(80, 20), line => line.Plain.Contains("1 of 2 prompts"));
    }

    [Theory]
    [InlineData(120, 30)]
    [InlineData(80, 24)]
    [InlineData(40, 12)]
    public void Paint_FillsTheScreenWithEqualWidthRows(int width, int height)
    {
        var picker = new QuickPromptPicker(Many(30, "\u5ba1\u67e5\u4ee3\u7801 line"), "shortcuts", null);

        var lines = picker.Paint(width, height);

        Assert.Equal(height, lines.Count);
        Assert.All(lines, line => Assert.Equal(width, TextWidth.Measure(line.Plain)));
        Assert.StartsWith("\u256d", lines[0].Plain, StringComparison.Ordinal);
        Assert.StartsWith("\u2570", lines[^1].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Paint_WideTerminalPutsTheListBesideThePreview()
    {
        var picker = new QuickPromptPicker(Many(3, "body text"), "shortcuts", null);

        var lines = picker.Paint(120, 30);

        Assert.Contains(lines, line => line.Plain.Contains("> prompt0") && line.Plain.Contains("body text"));
        Assert.Contains(lines, line => line.Plain.Contains("Prompts") && line.Plain.Contains("Preview"));
    }

    [Fact]
    public void Paint_NarrowTerminalStacksThePreviewUnderTheList()
    {
        var picker = new QuickPromptPicker(Many(3, "body text"), "shortcuts", null);

        var lines = picker.Paint(70, 24).ToList();

        var list = lines.FindIndex(line => line.Plain.Contains("> prompt0"));
        var preview = lines.FindIndex(line => line.Plain.Contains("Preview"));
        Assert.True(list >= 0 && preview > list);
    }

    [Fact]
    public void Paint_DrawsScrollbarsOnlyForContentThatOverflows()
    {
        var few = new QuickPromptPicker(Many(2, "short"), "shortcuts", null).Paint(120, 30);
        var many = new QuickPromptPicker(Many(60, string.Join('\n', Enumerable.Range(0, 80).Select(i => "row " + i))), "shortcuts", null)
            .Paint(120, 30);

        Assert.DoesNotContain(few, line => line.Plain.Contains('\u2503', StringComparison.Ordinal));
        Assert.Contains(many, line => line.Plain.Contains('\u2503', StringComparison.Ordinal));
        Assert.True(many.Count(line => line.Plain.Contains('\u2503', StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public void Paint_ListShowsAFirstLineSummaryAndFlagsBrokenEntries()
    {
        var picker = new QuickPromptPicker(
            [new("good", "\n  First line\nsecond", null), new("bad", "", "Empty")], "shortcuts", null);

        var lines = picker.Paint(120, 30);

        Assert.Contains(lines, line => line.Plain.Contains("> good  First line"));
        Assert.Contains(lines, line => line.Plain.Contains("bad  [disabled] Empty"));
    }

    private static QuickPromptOption[] Many(int count, string body) =>
        Enumerable.Range(0, count).Select(i => new QuickPromptOption("prompt" + i, body, null)).ToArray();

    private static InputKey Key(ConsoleKey key) => new(key, '\0', ConsoleModifiers.None);
}
