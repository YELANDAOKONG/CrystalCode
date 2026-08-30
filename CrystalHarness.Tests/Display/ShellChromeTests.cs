using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ShellChromeTests
{
    [Fact]
    public void StatusLine_ActivityPlainMatchesBullet()
    {
        var chrome = new ShellChrome
        {
            PlanMode = false,
            Approval = "Review",
            Activity = "Bash",
            Model = "deepseek-v4-flash",
            Usage = "CTX 3%"
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("• Bash", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Work", line.Plain, StringComparison.Ordinal);
        Assert.True(TextWidth.Measure(line.Plain) <= 120);
    }

    [Fact]
    public void StatusLine_FitsNarrowWidth()
    {
        var chrome = new ShellChrome
        {
            PlanMode = false,
            Approval = "Review",
            Activity = "Bash",
            Model = "deepseek-v4-flash",
            WorkspaceRoot = "/tmp/workspace/CrystalHarness",
            Usage = "CTX 3%  ·  29.3k in / 832 out",
            ToolCount = 6,
            Queued = 1
        };

        var line = chrome.StatusLine(80);

        Assert.True(TextWidth.Measure(line.Plain) <= 80);
    }

    [Fact]
    public void StatusLine_DoesNotRepeatComposerMode()
    {
        var chrome = new ShellChrome
        {
            PlanMode = false,
            Approval = "Review",
            Model = "deepseek-v4-flash",
            Usage = "CTX --"
        };

        var line = chrome.StatusLine(80);

        Assert.StartsWith("  Review", line.Plain, StringComparison.Ordinal);
        Assert.Contains("CTX --", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Work", line.Plain, StringComparison.Ordinal);
    }
}
