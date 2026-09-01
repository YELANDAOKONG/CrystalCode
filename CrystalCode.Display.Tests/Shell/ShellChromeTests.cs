using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

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
            WorkspaceRoot = "/tmp/workspace/CrystalCode",
            Usage = "CTX 3%  ·  29.3k IN / 832 OUT",
            ToolCount = 6,
            Queued = 1
        };

        var line = chrome.StatusLine(80);

        Assert.True(TextWidth.Measure(line.Plain) <= 80);
        Assert.Contains("6 Tools", line.Plain, StringComparison.Ordinal);
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

    [Fact]
    public void StatusLine_TitleCasesToolCount()
    {
        var one = new ShellChrome
        {
            Approval = "Review",
            Usage = "CTX --",
            ToolCount = 1
        };
        var many = new ShellChrome
        {
            Approval = "Review",
            Usage = "CTX --",
            ToolCount = 6
        };

        Assert.Contains("1 Tool", one.StatusLine(80).Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("1 tool", one.StatusLine(80).Plain, StringComparison.Ordinal);
        Assert.Contains("6 Tools", many.StatusLine(80).Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("6 tools", many.StatusLine(80).Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_AppendsTotalWhenWidthAllows()
    {
        var chrome = new ShellChrome
        {
            Approval = "Review",
            Usage = "CTX 3%  ·  769.1k IN / 13.8k OUT",
            UsageTotal = "783k Total"
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("769.1k IN / 13.8k OUT  ·  783k Total", line.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_OmitsTotalWhenNarrow()
    {
        var chrome = new ShellChrome
        {
            Approval = "Review",
            Usage = "CTX 3%  ·  769.1k IN / 13.8k OUT",
            UsageTotal = "783k Total"
        };
        var withoutTotal = new ShellChrome
        {
            Approval = "Review",
            Usage = "CTX 3%  ·  769.1k IN / 13.8k OUT"
        };
        var fitted = withoutTotal.StatusLine(120);
        var width = TextWidth.Measure(fitted.Plain);

        var line = chrome.StatusLine(width);

        Assert.DoesNotContain("Total", line.Plain, StringComparison.Ordinal);
        Assert.Contains("769.1k IN / 13.8k OUT", line.Plain, StringComparison.Ordinal);
        Assert.True(TextWidth.Measure(line.Plain) <= width);
    }

    [Fact]
    public void StatusLine_KeepsToolsWhenTotalDoesNotFit()
    {
        var chrome = new ShellChrome
        {
            Approval = "Review",
            Model = "deepseek-v4-flash",
            WorkspaceRoot = "/tmp/workspace/CrystalCode",
            Usage = "CTX 3%  ·  769.1k IN / 13.8k OUT",
            UsageTotal = "783k Total",
            ToolCount = 6
        };
        var withoutTotal = new ShellChrome
        {
            Approval = "Review",
            Model = "deepseek-v4-flash",
            WorkspaceRoot = "/tmp/workspace/CrystalCode",
            Usage = "CTX 3%  ·  769.1k IN / 13.8k OUT",
            ToolCount = 6
        };
        var fitted = withoutTotal.StatusLine(120);
        var width = TextWidth.Measure(fitted.Plain);

        var line = chrome.StatusLine(width);

        Assert.Contains("6 Tools", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Total", line.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressLine_EmptyWhenIdle()
    {
        var chrome = new ShellChrome { Progress = string.Empty };

        Assert.Equal(PaintLine.Blank, chrome.ProgressLine(80));
    }

    [Fact]
    public void ProgressLine_SitsIndependentOfStatusActivity()
    {
        var chrome = new ShellChrome
        {
            Approval = "Review",
            Activity = "Bash",
            Progress = "Awaiting Approval",
            Model = "deepseek-v4-flash",
            Usage = "CTX --"
        };

        var status = chrome.StatusLine(120);
        var progress = chrome.ProgressLine(80);

        Assert.Contains("• Bash", status.Plain, StringComparison.Ordinal);
        Assert.Contains("Review", status.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Awaiting Approval", status.Plain, StringComparison.Ordinal);
        Assert.Contains("Awaiting Approval", progress.Plain, StringComparison.Ordinal);
        Assert.Contains(ProgressSpinner.Frame(0), progress.Plain, StringComparison.Ordinal);
        Assert.Contains(" · 0s", progress.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("• Bash", progress.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void TickSpinner_AdvancesAfterInterval()
    {
        var chrome = new ShellChrome { Progress = "Thinking" };
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        chrome.TickSpinner(start);
        Assert.Contains(ProgressSpinner.Frame(0), chrome.ProgressLine(80).Plain, StringComparison.Ordinal);
        Assert.False(chrome.SpinnerDue(start));

        chrome.TickSpinner(start + (ProgressSpinner.Interval / 2));
        Assert.Contains(ProgressSpinner.Frame(0), chrome.ProgressLine(80).Plain, StringComparison.Ordinal);

        Assert.True(chrome.SpinnerDue(start + ProgressSpinner.Interval));
        chrome.TickSpinner(start + ProgressSpinner.Interval);
        Assert.Contains(ProgressSpinner.Frame(1), chrome.ProgressLine(80).Plain, StringComparison.Ordinal);
        Assert.DoesNotContain(ProgressSpinner.Frame(0), chrome.ProgressLine(80).Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressLine_ElapsedFollowsCurrentCaption()
    {
        var chrome = new ShellChrome { Progress = "Awaiting Approval" };
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        chrome.TickSpinner(start);
        Assert.Contains("Awaiting Approval · 0s", chrome.ProgressLine(80).Plain, StringComparison.Ordinal);

        chrome.TickSpinner(start.AddSeconds(5));
        Assert.Contains("Awaiting Approval · 5s", chrome.ProgressLine(80).Plain, StringComparison.Ordinal);

        chrome.TickSpinner(start.AddSeconds(138));
        Assert.Contains("Awaiting Approval · 2m18s", chrome.ProgressLine(80).Plain, StringComparison.Ordinal);

        chrome.Progress = "Running Command";
        chrome.TickSpinner(start.AddSeconds(140));
        Assert.Contains("Running Command · 0s", chrome.ProgressLine(80).Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("2m18s", chrome.ProgressLine(80).Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceProgress_DoesNotResetElapsed()
    {
        var chrome = new ShellChrome { Progress = "Retrying In 8s (Attempt 1)" };
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        chrome.TickSpinner(start);
        chrome.TickSpinner(start.AddSeconds(3));

        chrome.ReplaceProgress("Retrying In 5s (Attempt 1)");
        chrome.TickSpinner(start.AddSeconds(3));

        Assert.Contains("Retrying In 5s (Attempt 1) · 3s", chrome.ProgressLine(80).Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressLine_AppendsTokenEstimateAfterElapsed()
    {
        var chrome = new ShellChrome
        {
            Progress = "Thinking",
            TokenEstimate = "~12 Tokens"
        };
        chrome.TickSpinner(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var line = chrome.ProgressLine(80);

        Assert.Contains("Thinking · 0s · ~12 Tokens", line.Plain, StringComparison.Ordinal);
    }
}
