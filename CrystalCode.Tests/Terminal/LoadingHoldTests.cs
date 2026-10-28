using CrystalCode.Engine.Events;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

/// <summary>
/// While tools or plugins load in a live session, the composer stays
/// editable but nothing is submitted, so no keystroke becomes a queued turn
/// or an error and the draft is kept.
/// </summary>
public sealed class LoadingHoldTests
{
    private static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);

    [Theory]
    [InlineData(SessionActivity.LoadingTools)]
    [InlineData(SessionActivity.LoadingPlugins)]
    public async Task Enter_WhileLoadingKeepsTheDraft(SessionActivity activity)
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new ActivityChanged(activity));
        renderer.SeedComposer("draft");

        var held = await Dispatch(renderer, Enter);

        Assert.Null(held);
        projection.OnEvent(new ActivityChanged(SessionActivity.Idle));
        Assert.Equal("draft", await Dispatch(renderer, Enter));
    }

    [Theory]
    [InlineData(SessionActivity.Compacting)]
    [InlineData(SessionActivity.WaitingForModel)]
    public async Task Enter_DuringOtherWorkStillSubmits(SessionActivity activity)
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new ActivityChanged(activity));
        renderer.SeedComposer("next");

        Assert.Equal("next", await Dispatch(renderer, Enter));
    }

    [Fact]
    public async Task Typing_WhileLoadingStillEdits()
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new ActivityChanged(SessionActivity.LoadingTools));

        await Dispatch(renderer, new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        await Dispatch(renderer, new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));
        await Dispatch(renderer, Enter);
        projection.OnEvent(new ActivityChanged(SessionActivity.Idle));

        Assert.Equal("ab", await Dispatch(renderer, Enter));
    }

    [Fact]
    public async Task F7_WhileLoadingDoesNotOpenTheChildPicker()
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);
        projection.OnEvent(new ActivityChanged(SessionActivity.LoadingTools));

        Assert.Null(await Dispatch(renderer, new ConsoleKeyInfo('\0', ConsoleKey.F7, false, false, false)));
    }

    [Fact]
    public void LoadingProgressed_OutsideStartupNamesTheItemOnTheProgressRow()
    {
        var renderer = new SessionRenderer();
        var projection = new SessionProjection(renderer);

        projection.OnEvent(new ActivityChanged(SessionActivity.LoadingTools));
        projection.OnEvent(new LoadingProgressed(LoadingKind.ToolSet, "alpha"));

        Assert.Equal("Loading Tools  alpha", renderer.ChromeProgress);
        Assert.False(renderer.TryAddStartupItem("unused"));
        projection.OnEvent(new ActivityChanged(SessionActivity.Idle));
        Assert.Equal(string.Empty, renderer.ChromeProgress);
    }

    [Theory]
    [InlineData(LoadingKind.ToolSet, "Tool Set  a", "Loading Tools  a")]
    [InlineData(LoadingKind.Plugin, "Plugin  a", "Loading Plugins  a")]
    [InlineData(LoadingKind.McpServer, "MCP Server  a", "Loading Tools  a")]
    public void ProgressText_NamesEachKind(LoadingKind kind, string row, string caption)
    {
        Assert.Equal(row, ProgressText.Loading(kind, "a"));
        Assert.Equal(caption, ProgressText.LoadingItem(kind, "a"));
    }

    [Fact]
    public void IsInterruptKey_MatchesCtrlCOnly()
    {
        Assert.True(SessionRenderer.IsInterruptKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true)));
        Assert.True(SessionRenderer.IsInterruptKey(new ConsoleKeyInfo('\0', ConsoleKey.C, false, false, true)));
        Assert.False(SessionRenderer.IsInterruptKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false)));
        Assert.False(SessionRenderer.IsInterruptKey(new ConsoleKeyInfo('\0', ConsoleKey.C, false, true, true)));
    }

    private static Task<string?> Dispatch(SessionRenderer renderer, ConsoleKeyInfo key) =>
        renderer.DispatchBurstAsync([key], static () => false, CancellationToken.None, checkSize: false);
}
