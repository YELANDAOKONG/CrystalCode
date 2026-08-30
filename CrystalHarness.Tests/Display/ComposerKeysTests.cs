using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ComposerKeysTests
{
    [Fact]
    public void IsWordDeleteLeft_UnixControlBackspaceIsOneCharacter()
    {
        var unixBackspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, true);

        Assert.False(ComposerKeys.IsWordDeleteLeft(unixBackspace, windowsChords: false));
    }

    [Fact]
    public void IsWordDeleteLeft_WindowsControlBackspaceDeletesWord()
    {
        var windowsBackspace = new ConsoleKeyInfo('\u007f', ConsoleKey.Backspace, false, false, true);

        Assert.True(ComposerKeys.IsWordDeleteLeft(windowsBackspace, windowsChords: true));
    }

    [Fact]
    public void IsWordDeleteLeft_AltBackspaceDeletesWordOnEveryPlatform()
    {
        var altBackspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, true, false);

        Assert.True(ComposerKeys.IsWordDeleteLeft(altBackspace, windowsChords: false));
        Assert.True(ComposerKeys.IsWordDeleteLeft(altBackspace, windowsChords: true));
    }
}
