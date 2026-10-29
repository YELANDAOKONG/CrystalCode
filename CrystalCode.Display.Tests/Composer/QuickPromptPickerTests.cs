using CrystalCode.Display.Composer;
using CrystalCode.Display.Input;

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

    private static InputKey Key(ConsoleKey key) => new(key, '\0', ConsoleModifiers.None);
}
