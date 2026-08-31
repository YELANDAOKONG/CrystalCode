using CrystalHarness.Display.Paint;
using CrystalHarness.Display.Shell;

using Xunit;

namespace CrystalHarness.Tests.Display.Shell;

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

    [Fact]
    public void StatusLine_ShowsThinkingWhenConfigured()
    {
        var chrome = new ShellChrome
        {
            Approval = "Default",
            Thinking = "Think High",
            Model = "deepseek-v4-flash",
            Usage = "CTX --"
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("Think High", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Think Off", line.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_OmitsThinkingWhenEmpty()
    {
        var chrome = new ShellChrome
        {
            Approval = "Default",
            Model = "gpt-5.6-sol",
            Usage = "CTX --"
        };

        var line = chrome.StatusLine(80);

        Assert.DoesNotContain("Think", line.Plain, StringComparison.Ordinal);
    }
}
