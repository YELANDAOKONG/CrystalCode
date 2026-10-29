using CrystalCode.Display.Composer;
using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Composer;

public sealed class FullScreenEditorTests
{
    [Fact]
    public void Enter_InsertsANewlineAndF8Applies()
    {
        var editor = new FullScreenEditor("ab", planMode: false, customMode: null);

        editor.Handle(Key(ConsoleKey.Enter, '\r'));
        editor.Handle(Key(ConsoleKey.C, 'c'));
        editor.Handle(Key(ConsoleKey.F8, '\0'));

        Assert.True(editor.Closed);
        Assert.True(editor.Applied);
        Assert.Equal("ab\nc", editor.SubmissionText);
    }

    [Fact]
    public void CtrlS_Applies()
    {
        var editor = new FullScreenEditor("x", planMode: false, customMode: null);

        editor.Handle(new InputKey(ConsoleKey.S, '\u0013', ConsoleModifiers.Control));

        Assert.True(editor.Applied);
    }

    [Fact]
    public void Escape_CancelsWithoutApplying()
    {
        var editor = new FullScreenEditor("x", planMode: false, customMode: null);

        editor.Handle(Key(ConsoleKey.Escape, '\u001b'));

        Assert.True(editor.Closed);
        Assert.False(editor.Applied);
    }

    [Fact]
    public void QuestionMarkAndTab_AreText()
    {
        var editor = new FullScreenEditor(string.Empty, planMode: false, customMode: null);

        editor.Handle(Key(ConsoleKey.Oem2, '?'));
        editor.Handle(Key(ConsoleKey.Tab, '\t'));
        editor.Handle(Key(ConsoleKey.F8, '\0'));

        Assert.Equal("?\t", editor.SubmissionText);
    }

    [Fact]
    public void ImageMarkers_SurviveARoundTrip()
    {
        var submitted = "a\u2063[Image #3]b";
        var editor = new FullScreenEditor(submitted, planMode: false, customMode: null);

        Assert.Equal(submitted, editor.SubmissionText);
    }

    [Fact]
    public void Paint_FillsTheScreenAndPlacesTheCursorBelowTheHeader()
    {
        var editor = new FullScreenEditor("hello", planMode: false, customMode: null);

        var view = editor.Paint(40, 10);

        Assert.Equal(10, view.Lines.Count);
        Assert.Equal(1, view.CursorRow);
        Assert.Contains("Line 1/1", view.Lines[0].Plain);
    }

    [Fact]
    public void Paint_ShowsTheTextWithoutTheModePrompt()
    {
        var editor = new FullScreenEditor("hello", planMode: false, customMode: null);

        var view = editor.Paint(40, 10);

        Assert.StartsWith("\u2502 hello ", view.Lines[1].Plain);
        Assert.Equal(7, view.CursorColumn);
    }

    [Fact]
    public void Paint_KeepsTheBorderIntactWhileTheTextScrolls()
    {
        var text = string.Join('\n', Enumerable.Range(1, 40).Select(number => "line " + number + " \u4e2d\u6587"));
        var editor = new FullScreenEditor(text, planMode: false, customMode: null);

        var view = editor.Paint(30, 8);

        Assert.Equal(8, view.Lines.Count);
        Assert.StartsWith("\u256d", view.Lines[0].Plain);
        Assert.StartsWith("\u2570", view.Lines[7].Plain);
        for (var row = 0; row < view.Lines.Count; row++)
        {
            Assert.Equal(30, TextWidth.Measure(view.Lines[row].Plain));
        }

        for (var row = 1; row < 7; row++)
        {
            Assert.StartsWith("\u2502", view.Lines[row].Plain);
            Assert.EndsWith("\u2502", view.Lines[row].Plain);
        }

        Assert.Contains("line 40", view.Lines[6].Plain);
        Assert.InRange(view.CursorRow, 1, 6);
    }

    [Fact]
    public void Paint_WrapsLongLinesInsideTheBorder()
    {
        var editor = new FullScreenEditor(new string('x', 100), planMode: false, customMode: null);

        var view = editor.Paint(24, 8);

        foreach (var line in view.Lines)
        {
            Assert.Equal(24, TextWidth.Measure(line.Plain));
        }

        Assert.InRange(view.CursorColumn, 2, 22);
    }

    [Fact]
    public void Wheel_ScrollsTheTextAndKeepsTheBorder()
    {
        var text = string.Join('\n', Enumerable.Range(1, 60).Select(number => "line " + number));
        var editor = new FullScreenEditor(text, planMode: false, customMode: null);
        editor.Paint(30, 8);

        editor.Handle(new InputWheel(30));
        var view = editor.Paint(30, 8);

        Assert.DoesNotContain(view.Lines, line => line.Plain.Contains("line 60"));
        Assert.Contains(view.Lines, line => line.Plain.Contains("line 30"));
        Assert.Contains("Line 30/60", view.Lines[0].Plain);
        AssertBordered(view, 30);

        editor.Handle(new InputWheel(-1000));
        view = editor.Paint(30, 8);

        Assert.Contains(view.Lines, line => line.Plain.Contains("line 60"));
        AssertBordered(view, 30);

        editor.Handle(new InputWheel(1000));
        view = editor.Paint(30, 8);

        Assert.Contains(view.Lines, line => line.Plain.Contains("line 1 "));
        AssertBordered(view, 30);
    }

    [Fact]
    public void PageKeys_MoveByOneWindow()
    {
        var text = string.Join('\n', Enumerable.Range(1, 60).Select(number => "line " + number));
        var editor = new FullScreenEditor(text, planMode: false, customMode: null);
        editor.Paint(30, 8);

        editor.Handle(Key(ConsoleKey.PageUp, '\0'));
        var view = editor.Paint(30, 8);

        Assert.Contains("Line 55/60", view.Lines[0].Plain);
        editor.Handle(Key(ConsoleKey.PageDown, '\0'));
        view = editor.Paint(30, 8);
        Assert.Contains("Line 60/60", view.Lines[0].Plain);
        AssertBordered(view, 30);
    }

    private static void AssertBordered(ComposerView view, int width)
    {
        Assert.StartsWith("\u256d", view.Lines[0].Plain);
        Assert.StartsWith("\u2570", view.Lines[^1].Plain);
        foreach (var line in view.Lines)
        {
            Assert.Equal(width, TextWidth.Measure(line.Plain));
        }

        for (var row = 1; row < view.Lines.Count - 1; row++)
        {
            Assert.StartsWith("\u2502", view.Lines[row].Plain);
            Assert.EndsWith("\u2502", view.Lines[row].Plain);
        }
    }

    [Fact]
    public void Paste_InsertsText()
    {
        var editor = new FullScreenEditor(string.Empty, planMode: false, customMode: null);

        editor.Handle(new InputPaste("one\ntwo"));

        Assert.Equal("one\ntwo", editor.SubmissionText);
    }

    private static InputKey Key(ConsoleKey key, char ch) =>
        new(key, ch, ConsoleModifiers.None);
}
