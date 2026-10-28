using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class StartupPageTests
{
    [Fact]
    public void Paint_ShowsTitlePhaseItemsAndHint()
    {
        var frame = StartupPage.Paint(60, 12, "x", "Loading Tools", ["Tool Set  alpha", "Tool Set  beta"]);

        Assert.Equal(12, frame.Count);
        Assert.Contains(StartupPage.Title, frame[0].Plain, StringComparison.Ordinal);
        Assert.Contains("x  Loading Tools", frame[2].Plain, StringComparison.Ordinal);
        Assert.Contains("Tool Set  alpha", frame[4].Plain, StringComparison.Ordinal);
        Assert.Contains("Tool Set  beta", frame[5].Plain, StringComparison.Ordinal);
        Assert.Contains(StartupPage.Hint, frame[7].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Paint_WithoutPhaseSaysPreparing()
    {
        var frame = StartupPage.Paint(40, 8, "x", string.Empty, []);

        Assert.Contains("Preparing", frame[2].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Paint_KeepsTheNewestItemsWhenTheListIsTaller()
    {
        var items = Enumerable.Range(1, 20).Select(static index => "Plugin  p" + index).ToArray();

        var frame = StartupPage.Paint(40, 10, "x", "Loading Plugins", items);

        var listed = frame.Select(static line => line.Plain).Where(static text => text.Contains("Plugin  p", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, listed.Length);
        Assert.EndsWith("p20", listed[^1].TrimEnd(), StringComparison.Ordinal);
        Assert.Contains(StartupPage.Hint, frame[^1].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Paint_StripsControlCharactersFromNames()
    {
        var frame = StartupPage.Paint(40, 8, "x", "Loading Tools", ["Tool Set  bad\u001b[2Jname"]);

        Assert.DoesNotContain('\u001b', frame[4].Plain);
        Assert.DoesNotContain('\u001b', frame[4].Markup);
    }

    [Fact]
    public void Paint_FitsEveryRowToTheWidth()
    {
        var frame = StartupPage.Paint(10, 8, "x", "Loading Tools", ["Tool Set  a-very-long-directory-name"]);

        Assert.All(frame, static line => Assert.True(line.Plain.Length <= 10, line.Plain));
    }
}
