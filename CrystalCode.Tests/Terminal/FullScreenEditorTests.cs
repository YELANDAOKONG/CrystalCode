using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class FullScreenEditorTests
{
    [Fact]
    public async Task F8_EditsTheDraftAndApplyKeepsItInTheComposer()
    {
        using var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");

        await Press(renderer, ConsoleKey.F8);
        await Type(renderer, " more");
        Assert.Null(await Press(renderer, ConsoleKey.Enter));
        Assert.Null(await Press(renderer, ConsoleKey.F8));

        Assert.Equal("draft more\n", await Press(renderer, ConsoleKey.Enter));
    }

    [Fact]
    public async Task Escape_LeavesTheDraftUnchanged()
    {
        using var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");

        await Press(renderer, ConsoleKey.F8);
        await Type(renderer, "xyz");
        await Press(renderer, ConsoleKey.Escape);

        Assert.Equal("draft", await Press(renderer, ConsoleKey.Enter));
    }

    [Fact]
    public async Task EditCommand_OpensEmptyEditor()
    {
        using var renderer = new SessionRenderer();
        renderer.SeedComposer("/edit");

        Assert.Null(await Press(renderer, ConsoleKey.Enter));
        await Type(renderer, "hi");
        await Press(renderer, ConsoleKey.F8);

        Assert.Equal("hi", await Press(renderer, ConsoleKey.Enter));
    }

    [Fact]
    public async Task F8_DoesNothingWhileTheComposerIsPaused()
    {
        using var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");
        renderer.PauseComposer();
        await Press(renderer, ConsoleKey.F8);
        renderer.ResumeComposer();

        Assert.Equal("draft", await Press(renderer, ConsoleKey.Enter));
    }

    private static async Task Type(SessionRenderer renderer, string text)
    {
        foreach (var ch in text)
        {
            await renderer.DispatchBurstAsync([new(ch, default, false, false, false)],
                () => false, CancellationToken.None, checkSize: false);
        }
    }

    private static Task<string?> Press(SessionRenderer renderer, ConsoleKey key) =>
        renderer.DispatchBurstAsync([new('\0', key, false, false, false)],
            () => false, CancellationToken.None, checkSize: false);
}
