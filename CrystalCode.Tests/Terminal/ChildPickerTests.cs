using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Engine.Home;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ChildPickerTests
{
    [Theory]
    [InlineData(false, ConsoleKey.Escape)]
    [InlineData(true, ConsoleKey.Escape)]
    [InlineData(false, ConsoleKey.F7)]
    [InlineData(true, ConsoleKey.F7)]
    public async Task ChooseAsync_CancelPreservesFocusAndParentDraft(bool childView, ConsoleKey key)
    {
        using var renderer = new SessionRenderer();
        var child = new ChildSessionDocument { Id = "child-one", Label = "research", Agent = "explore" };
        renderer.SetChildSessions([child], childView ? child.Id : null);
        renderer.SeedComposer("parent draft");
        var picker = new ChildPicker(renderer, (scrollPlainArrows, _, _) =>
        {
            Assert.False(scrollPlainArrows);
            Assert.True(renderer.OverlayVisible);
            return Task.FromResult(new InputKey(key, '\0', ConsoleModifiers.None));
        });

        Assert.Null(await picker.ChooseAsync([child], CancellationToken.None));
        Assert.Equal(childView, renderer.ChildViewActive);
        Assert.Equal(childView ? child.Id : null, renderer.ChildFocus);
        Assert.False(renderer.OverlayVisible);
        renderer.SetChildSessions([child], null);
        Assert.Equal("parent draft", await renderer.DispatchBurstAsync(
            [new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)],
            static () => false, CancellationToken.None, checkSize: false));
    }

    [Fact]
    public async Task ChooseAsync_RefreshesLiveStatusAndUsageWithoutMovingSelectionToAnotherChild()
    {
        using var renderer = new SessionRenderer();
        var first = new ChildSessionDocument { Id = "first", Agent = "explore", Status = "running" };
        var second = new ChildSessionDocument { Id = "second", Agent = "explore", Status = "running" };
        renderer.SetChildSessions([first, second], null);
        var picker = new ChildPicker(renderer, (_, refresh, _) =>
        {
            renderer.SetChildSessions([first with
            {
                Status = "completed",
                CumulativeUsage = new SessionUsageDocument { InputTokenCount = 230, OutputTokenCount = 42 }
            }, second], null);
            refresh();
            var field = typeof(SessionRenderer).GetField("_overlayWidget",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var widget = Assert.IsAssignableFrom<Spectre.Console.Rendering.IRenderable>(field.GetValue(renderer));
            var lines = WidgetPaint.Lines(widget, 120);
            Assert.Contains(lines, line => line.Plain.Contains(">   explore  completed", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Plain.Contains("IN 230 / OUT 42", StringComparison.Ordinal));
            return Task.FromResult(new InputKey(ConsoleKey.Enter, '\r', ConsoleModifiers.None));
        });

        Assert.Equal(first.Id, await picker.ChooseAsync([first, second], CancellationToken.None));
        Assert.False(renderer.OverlayVisible);
    }

    [Fact]
    public async Task ChooseAsync_DoesNotResurrectChildrenAfterTheLiveListIsCleared()
    {
        using var renderer = new SessionRenderer();
        var child = new ChildSessionDocument { Id = "child", Agent = "explore", Status = "running" };
        renderer.SetChildSessions([child], null);
        var reads = 0;
        var picker = new ChildPicker(renderer, (_, refresh, _) =>
        {
            reads++;
            renderer.SetChildSessions([], null);
            refresh();
            return Task.FromResult(new InputKey(reads == 1 ? ConsoleKey.Enter : ConsoleKey.Escape,
                '\0', ConsoleModifiers.None));
        });

        Assert.Null(await picker.ChooseAsync([child], CancellationToken.None));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Filter_PrioritizesActiveAndMatchesIdentityLabelAgentAndStatus()
    {
        ChildSessionDocument[] children =
        [
            new() { Id = "done", Label = "parser", Agent = "work", Status = "completed" },
            new() { Id = "live", Label = "research", Agent = "explore", Status = "running" },
            new() { Id = "waiting", Agent = "general", Status = "queued" }
        ];
        Assert.Equal(["live", "waiting", "done"], ChildPicker.Filter(children, "", false).Select(child => child.Id));
        Assert.Equal(["live", "waiting"], ChildPicker.Filter(children, "", true).Select(child => child.Id));
        foreach (var query in new[] { "DONE", "parser", "work", "completed" })
        {
            Assert.Equal("done", Assert.Single(ChildPicker.Filter(children, query, false)).Id);
        }
        Assert.Empty(ChildPicker.Filter(children, "parser", true));
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(120, 40)]
    public void Render_KeepsSelectionVisibleWithinSmallTerminalsAndEscapesModelText(int width, int height)
    {
        var children = Enumerable.Range(0, 30).Select(index => new ChildSessionDocument
        {
            Id = "child-" + index,
            Label = "[untrusted markup] " + new string('x', width),
            Agent = "explore",
            Status = "running"
        }).ToArray();
        var lines = WidgetPaint.Lines(ChildPicker.Render(children, "[search]", 29, "child-29", false, width, height), width);
        Assert.True(lines.Count <= height);
        Assert.Contains(lines, line => line.Plain.Contains("child-29", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Plain.Contains("child-0 ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("[untrusted markup]", StringComparison.Ordinal));
    }
}
