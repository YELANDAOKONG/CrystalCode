using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ResumePickerTests
{
    [Theory]
    [InlineData("FIX")]
    [InlineData("first message")]
    [InlineData("beta")]
    [InlineData("newest")]
    public void Search_PreservesUpdateOrderAndMatchesTitlePreviewWorkspaceAndId(string query)
    {
        ResumeOption[] sessions =
        [
            new("newest", "Fix parser", "First message", "/tmp/beta", null, 2),
            new("older", "Fix tests", "First message", "/tmp/beta", null, 1),
            new("oldest", "Review", "Other message", "/tmp/alpha", null, 1)
        ];
        var picker = new ResumePicker(sessions, null, listWorkspace: false);

        picker.Handle(new InputPaste(query));
        picker.Handle(Key(ConsoleKey.Enter));

        Assert.Equal("newest", picker.ChosenId);
    }

    [Theory]
    [InlineData(80, 24, 9)]
    [InlineData(120, 40, 17)]
    public void Paint_FillsTheFrameAndShowsTheScrollbarOnlyWhenNeeded(int width, int height, int visible)
    {
        var few = new ResumePicker(Sessions(visible), null, listWorkspace: false);
        var many = new ResumePicker(Sessions(40), "id0", listWorkspace: true);

        var frame = many.Paint(width, height);

        Assert.Equal(height, frame.Count);
        Assert.Equal(visible, frame.Count(IsSessionTitle));
        Assert.Contains(frame, line => line.Plain.Contains('┃'));
        Assert.DoesNotContain(few.Paint(width, height), line => line.Plain.Contains('┃'));
        Assert.Contains(frame, line => line.Plain.Contains("/tmp/work", StringComparison.Ordinal));
        Assert.Contains(frame, line => line.Plain.Contains("id0", StringComparison.Ordinal)
            && line.Plain.Contains("current", StringComparison.Ordinal));
        Assert.All(frame, line => Assert.InRange(TextWidth.Measure(line.Plain), 0, width));
    }

    [Fact]
    public void Wheel_ScrollsBothWaysAndKeepsTheSelectedSessionVisibleThroughResize()
    {
        var picker = new ResumePicker(Sessions(40), null, listWorkspace: false);
        picker.Paint(80, 24);

        picker.Handle(new InputWheel(-InputWheel.LineStep));
        picker.Handle(new InputWheel(-InputWheel.LineStep));
        picker.Handle(new InputWheel(InputWheel.LineStep));
        var frame = picker.Paint(80, 24);

        Assert.StartsWith("  Fix 3", frame.First(IsSessionTitle).Plain);
        Assert.Contains(frame, line => line.Plain.StartsWith("> Fix 6 ", StringComparison.Ordinal));
        frame = picker.Paint(80, 10);
        Assert.StartsWith("> Fix 6", frame.Last(IsSessionTitle).Plain);
        picker.Handle(Key(ConsoleKey.Enter));
        Assert.Equal("id6", picker.ChosenId);
    }

    [Fact]
    public void PageDown_MovesTheSelectionByTheVisibleListSize()
    {
        var picker = new ResumePicker(Sessions(40), null, listWorkspace: false);
        picker.Paint(80, 24);

        picker.Handle(Key(ConsoleKey.PageDown));
        picker.Handle(Key(ConsoleKey.Enter));

        Assert.Equal("id9", picker.ChosenId);
    }

    [Fact]
    public void NoMatches_EnterKeepsThePickerOpenAndEscapeDismissesIt()
    {
        var picker = new ResumePicker(Sessions(40), null, listWorkspace: false);
        picker.Paint(80, 24);
        picker.Handle(new InputPaste("missing"));

        picker.Handle(new InputWheel(-InputWheel.LineStep));
        picker.Handle(Key(ConsoleKey.Enter));

        Assert.False(picker.Closed);
        Assert.Contains(picker.Paint(80, 24), line => line.Plain == "No matching sessions");

        picker.Handle(Key(ConsoleKey.Escape));

        Assert.True(picker.Closed);
        Assert.Null(picker.ChosenId);
    }

    private static bool IsSessionTitle(PaintLine line) =>
        line.Plain.StartsWith("  Fix ", StringComparison.Ordinal)
        || line.Plain.StartsWith("> Fix ", StringComparison.Ordinal);

    private static ResumeOption[] Sessions(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ResumeOption($"id{i}", $"Fix {i}", "First message", "/tmp/work", null, 1))
            .ToArray();

    private static InputKey Key(ConsoleKey key) => new(key, '\0', ConsoleModifiers.None);
}
