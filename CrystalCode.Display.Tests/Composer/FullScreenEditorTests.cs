using CrystalCode.Display.Composer;
using CrystalCode.Display.Input;

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
    public void Paste_InsertsText()
    {
        var editor = new FullScreenEditor(string.Empty, planMode: false, customMode: null);

        editor.Handle(new InputPaste("one\ntwo"));

        Assert.Equal("one\ntwo", editor.SubmissionText);
    }

    private static InputKey Key(ConsoleKey key, char ch) =>
        new(key, ch, ConsoleModifiers.None);
}
