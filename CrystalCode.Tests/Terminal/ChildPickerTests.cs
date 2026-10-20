using CrystalCode.Display.Paint;
using CrystalCode.Engine.Home;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ChildPickerTests
{
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
