using CrystalCode.Engine.Home;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class QuickPromptTests
{
    [Fact]
    public async Task Fill_InsertsAtCursorAndKeepsImageProvenance()
    {
        using var renderer = Create();
        renderer.SeedComposer("ab");
        await Press(renderer, ConsoleKey.LeftArrow);
        renderer.OnImagePasteAsync = _ => Task.FromResult<string?>("[Image #7]");
        await renderer.DispatchBurstAsync([new('\u0016', ConsoleKey.V, false, false, true)],
            () => false, CancellationToken.None, checkSize: false);
        await Press(renderer, ConsoleKey.F6);
        Assert.Null(await Press(renderer, ConsoleKey.Enter));
        Assert.Equal("a\u2063[Image #7]中文\n第二行b", await Press(renderer, ConsoleKey.Enter));
    }

    [Fact]
    public async Task Send_LeavesDraftAndCancelLeavesItUnchanged()
    {
        using var renderer = Create();
        renderer.SeedComposer("draft");
        await Press(renderer, ConsoleKey.F6);
        await Press(renderer, ConsoleKey.Tab);
        Assert.Equal("中文\n第二行", await Press(renderer, ConsoleKey.Enter));
        await Press(renderer, ConsoleKey.F6);
        await Press(renderer, ConsoleKey.Escape);
        Assert.Equal("draft", await Press(renderer, ConsoleKey.Enter));
    }

    [Fact]
    public async Task Send_PreservesDraftImageAndReopeningDefaultsToFill()
    {
        using var renderer = Create();
        renderer.SeedComposer("draft");
        renderer.OnImagePasteAsync = _ => Task.FromResult<string?>("[Image #7]");
        await renderer.DispatchBurstAsync([new('\u0016', ConsoleKey.V, false, false, true)],
            () => false, CancellationToken.None, checkSize: false);
        await Press(renderer, ConsoleKey.F6);
        await Press(renderer, ConsoleKey.Tab);
        Assert.Equal("中文\n第二行", await Press(renderer, ConsoleKey.Enter));
        await Press(renderer, ConsoleKey.F6);
        Assert.Null(await Press(renderer, ConsoleKey.Enter));
        Assert.Equal("draft\u2063[Image #7]中文\n第二行", await Press(renderer, ConsoleKey.Enter));
    }

    [Fact]
    public async Task F6_DoesNotLoadDuringModalOrPause()
    {
        using var renderer = Create();
        renderer.LoadQuickPromptsAsync = _ => throw new InvalidOperationException("Unexpected load");
        renderer.SetOverlay(["approval"]);
        await Press(renderer, ConsoleKey.F6);
        renderer.ClearOverlay();
        renderer.PauseComposer();
        await Press(renderer, ConsoleKey.F6);
        renderer.ResumeComposer();
    }

    [Fact]
    public async Task RawF6_OpensPicker()
    {
        using var renderer = Create();
        var burst = "\u001b[17~".Select(ch => new ConsoleKeyInfo(ch, default, false, false, false)).ToArray();
        await renderer.DispatchBurstAsync(burst, () => false, CancellationToken.None, checkSize: false);
        await Press(renderer, ConsoleKey.Enter);
        Assert.Equal("中文\n第二行", await Press(renderer, ConsoleKey.Enter));
    }

    private static SessionRenderer Create() => new()
    {
        LoadQuickPromptsAsync = _ => Task.FromResult(new QuickPromptCatalog("shortcuts",
            [new QuickPrompt("test", "中文\n第二行")]))
    };

    private static Task<string?> Press(SessionRenderer renderer, ConsoleKey key) =>
        renderer.DispatchBurstAsync([new('\0', key, false, false, false)],
            () => false, CancellationToken.None, checkSize: false);
}
