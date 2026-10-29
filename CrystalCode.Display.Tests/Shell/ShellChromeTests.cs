using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

[Collection("Theme")]
public sealed class ShellChromeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusLine_ShowsSelectedContextWindow(bool custom)
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = custom,
            StatusLineFields = ["thinking", "context-window", "context-used"],
            Thinking = "Think High",
            ContextWindow = "Context 272K",
            Usage = "CTX 25%",
            ContextUsed = "CTX 25%"
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("Think High  ·  Context 272K  ·  CTX 25%", line.Plain, StringComparison.Ordinal);
        Assert.True(TextWidth.Measure(chrome.StatusLine(20).Plain) <= 20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusLine_ShowsPendingSettingsBesideTheActiveOnes(bool custom)
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = custom,
            StatusLineFields = ["approval", "model", "workspace"],
            Approval = "Default",
            Model = "a-model-name-that-is-long",
            WorkspaceRoot = "/a/long/workspace/path",
            Pending = "Next: Plan, Approval Full"
        };

        Assert.Contains("Default", chrome.StatusLine(120).Plain, StringComparison.Ordinal);
        Assert.Contains("Next: Plan, Approval Full", chrome.StatusLine(120).Plain, StringComparison.Ordinal);
        Assert.Contains("Next:", chrome.StatusLine(40).Plain, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusLine_PaintsApprovalWithItsModeColor(bool custom)
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = custom,
            StatusLineFields = ["approval"],
            Approval = "Full"
        };

        Assert.Contains($"[{Theme.ApprovalFull}]Full[/]", chrome.StatusLine(120).Markup, StringComparison.Ordinal);

        chrome.Approval = "Review";

        Assert.Contains($"[{Theme.ApprovalReview}]Review[/]", chrome.StatusLine(120).Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StatusLine_PaintsApprovalWithChromeWhenApprovalColorsAreOff(bool custom)
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = custom,
            StatusLineFields = ["approval"],
            Approval = "Full",
            ApprovalColors = false
        };

        Assert.Contains($"[{Theme.Chrome}]Full[/]", chrome.StatusLine(120).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_CustomModeUsesConfiguredFieldsInOrder()
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = true,
            StatusLineFields = ["context-left", "session-total", "model"],
            ContextLeft = "CTX 37% Left",
            SessionTotal = "801k Session",
            Model = "gpt-5.6-sol",
            Approval = "Review",
            Usage = "legacy usage"
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("CTX 37% Left  ·  801k Session  ·  gpt-5.6-sol", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Review", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy usage", line.Plain, StringComparison.Ordinal);
    }

    private static ShellChrome Squeezable() => new()
    {
        CustomStatusLine = true,
        StatusLineFields = ["approval", "model", "elapsed", "session-total"],
        Approval = "Review",
        Model = "gpt-5.6-sol",
        Elapsed = "12s",
        SessionTotal = "801k Session"
    };

    [Fact]
    public void StatusLine_CustomModeHidesFromTheEndByDefault()
    {
        var line = Squeezable().StatusLine(34);

        Assert.Equal("  Review  ·  gpt-5.6-sol  ·  12s", line.Plain);
    }

    [Fact]
    public void StatusLine_CustomDropOrderHidesListedFieldsFirst()
    {
        var chrome = Squeezable();
        chrome.StatusLineDropOrder = ["model", "elapsed"];

        Assert.Equal("  Review  ·  12s  ·  801k Session", chrome.StatusLine(36).Plain);
        Assert.Equal("  Review  ·  801k Session", chrome.StatusLine(26).Plain);
    }

    [Fact]
    public void StatusLine_CustomKeepFieldIsNeverHidden()
    {
        var chrome = Squeezable();
        chrome.StatusLineRules = [new("session-total", Keep: true)];

        var line = chrome.StatusLine(18);

        Assert.Contains("801k Session", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Review", line.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_CustomHideBelowRemovesTheFieldOnNarrowTerminals()
    {
        var chrome = Squeezable();
        chrome.StatusLineRules = [new("elapsed", HideBelow: 100)];

        Assert.DoesNotContain("12s", chrome.StatusLine(99).Plain, StringComparison.Ordinal);
        Assert.Contains("12s", chrome.StatusLine(100).Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_ShowsAdmittedPluginTextAndSkipsEmptyFields()
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = true,
            StatusLineFields = ["model", "branch", "quiet"],
            Model = "gpt-5.6-sol",
            PluginFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["branch"] = "main\nline",
                ["quiet"] = "   "
            }
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("gpt-5.6-sol  ·  main line", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("quiet", line.Plain, StringComparison.Ordinal);
    }

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

    [Theory]
    [InlineData("CTX 7.6%", "Chrome")]
    [InlineData("CTX 65.2%  ·  1k IN / 2 OUT", "Warning")]
    [InlineData("CTX 85.0%", "Fail")]
    public void StatusLine_ColorsDecimalContextPercent(string usage, string color)
    {
        var chrome = new ShellChrome { Usage = usage };
        var expected = color switch
        {
            "Warning" => Theme.Warning,
            "Fail" => Theme.Fail,
            _ => Theme.Chrome
        };

        var markup = chrome.StatusLine(120).Markup;

        Assert.Contains($"[{expected}]{usage}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_ColorsInputOutputTotalPathToolsAndElapsedOneByOne()
    {
        try
        {
            Theme.TryApply(
                new Dictionary<string, string>
                {
                    ["status.input"] = "teal",
                    ["status.output"] = "orchid",
                    ["status.total"] = "gold1",
                    ["status.path"] = "blue",
                    ["status.tools"] = "green",
                    ["status.elapsed"] = "red"
                },
                out _);
            var chrome = new ShellChrome
            {
                Usage = "CTX 6.6%  ·  1.1M IN / 7917 OUT",
                UsageTotal = "1.1M Total",
                WorkspaceRoot = "/a/workspace",
                ToolCount = 3,
                Elapsed = "12s"
            };

            var markup = chrome.StatusLine(200).Markup;

            Assert.Contains($"[{Theme.Chrome}]CTX 6.6%  ·  [/]", markup, StringComparison.Ordinal);
            Assert.Contains("[teal]1.1M IN[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[orchid]7917 OUT[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[gold1]1.1M Total[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[blue]", markup, StringComparison.Ordinal);
            Assert.Contains("[green]3 Tools[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[red]12s[/]", markup, StringComparison.Ordinal);
            Assert.Contains("CTX 6.6%  ·  1.1M IN / 7917 OUT", chrome.StatusLine(200).Plain, StringComparison.Ordinal);
    
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Fact]
    public void StatusLine_WithoutStatusColorsKeepsOneUsageColor()
    {
        Theme.Reset();
        var chrome = new ShellChrome
        {
            Usage = "CTX 6.6%  ·  1.1M IN / 7917 OUT",
            UsageTotal = "1.1M Total"
        };

        var markup = chrome.StatusLine(200).Markup;

        Assert.Contains($"[{Theme.Chrome}]CTX 6.6%  ·  1.1M IN / 7917 OUT[/]", markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Chrome}]1.1M Total[/]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_ColorsCustomStatusItemsFromTheTheme()
    {
        try
        {
            Theme.TryApply(
                new Dictionary<string, string>
                {
                    ["status.input"] = "teal",
                    ["status.output"] = "orchid",
                    ["status.total"] = "gold1",
                    ["status.path"] = "blue",
                    ["status.tools"] = "green",
                    ["status.elapsed"] = "red"
                },
                out _);
            var chrome = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["session-input", "request-output", "session-total", "workspace", "tools", "elapsed"],
                SessionInput = "1.1M Session In",
                RequestOutput = "7917 Request Out",
                SessionTotal = "1.1M Session",
                WorkspaceRoot = "/a/workspace",
                ToolCount = 2,
                Elapsed = "9s"
            };

            var markup = chrome.StatusLine(200).Markup;

            Assert.Contains("[teal]1.1M Session In[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[orchid]7917 Request Out[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[gold1]1.1M Session[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[blue]", markup, StringComparison.Ordinal);
            Assert.Contains("[green]2 Tools[/]", markup, StringComparison.Ordinal);
            Assert.Contains("[red]9s[/]", markup, StringComparison.Ordinal);
    
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Fact]
    public void StatusLine_ColorsCustomContextFieldsFromUsedPercent()
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = true,
            StatusLineFields = ["context-used", "context-left"],
            ContextUsed = "CTX 85.0%",
            ContextLeft = "CTX 15.0% Left"
        };

        var markup = chrome.StatusLine(120).Markup;

        Assert.Contains($"[{Theme.Fail}]CTX 85.0%", markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Fail}]CTX 15.0% Left", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_ColorsThinkingTierAndSpeedFromThemeRules()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["thinking"] = [new GaugeRule(null, null, ["high"], null, "orange1")],
            ["tier"] = [new GaugeRule(null, null, [], "^pri", "gold1")],
            ["speed"] = [new GaugeRule(null, null, ["*"], null, "green")]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);
        try
        {
            var chrome = new ShellChrome { Thinking = "Think High", ServiceTier = "Tier Priority", Speed = "Speed Fast" };
            var custom = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["thinking", "tier", "speed"],
                Thinking = "Think Low",
                ServiceTier = "Tier Priority",
                Speed = "Speed Fast"
            };

            var markup = chrome.StatusLine(120).Markup;
            var customMarkup = custom.StatusLine(120).Markup;

            Assert.Contains("[orange1]Think High", markup, StringComparison.Ordinal);
            Assert.Contains("[gold1]Tier Priority", markup, StringComparison.Ordinal);
            Assert.Contains("[green]Speed Fast", markup, StringComparison.Ordinal);
            Assert.Contains($"[{Theme.Chrome}]Think Low", customMarkup, StringComparison.Ordinal);
            Assert.Contains("[gold1]Tier Priority", customMarkup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Fact]
    public void StatusLine_ColorsModelContextWindowAndPromptSetFromThemeRules()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["model"] = [new GaugeRule(null, null, [], "^gpt", "green")],
            ["context-window"] = [new GaugeRule(null, null, ["1M"], null, "gold1")],
            ["prompt-set"] = [new GaugeRule(null, null, ["review"], null, "red")]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);
        try
        {
            var chrome = new ShellChrome { Model = "gpt-5", ContextWindow = "Context 1M", PromptSet = "Review" };
            var custom = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["model", "context-window", "prompt-set"],
                Model = "claude",
                ContextWindow = "Context 1M",
                PromptSet = "Review"
            };

            var markup = chrome.StatusLine(120).Markup;
            var customMarkup = custom.StatusLine(120).Markup;

            Assert.Contains("[green]gpt-5", markup, StringComparison.Ordinal);
            Assert.Contains("[gold1]Context 1M", markup, StringComparison.Ordinal);
            Assert.Contains("[red]Prompt Review", markup, StringComparison.Ordinal);
            Assert.Contains($"[{Theme.Chrome}]claude", customMarkup, StringComparison.Ordinal);
            Assert.Contains("[gold1]Context 1M", customMarkup, StringComparison.Ordinal);
            Assert.Contains("[red]Prompt Review", customMarkup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Theory]
    [InlineData("Context 128K", "grey50")]
    [InlineData("Context 200K", "yellow")]
    [InlineData("Context 400K", "yellow")]
    [InlineData("Context 1M", "gold1")]
    [InlineData("Context 1500000", "gold1")]
    [InlineData("Context 2M", "gold1")]
    public void StatusLine_ColorsContextWindowByTokenRange(string window, string expected)
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["context-window"] =
            [
                new GaugeRule(1_000_000, null, [], null, "gold1"),
                new GaugeRule(200_000, 999_999, [], null, "yellow")
            ]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);
        try
        {
            var chrome = new ShellChrome { ContextWindow = window };
            var custom = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["context-window"],
                ContextWindow = window
            };

            var expectedStyle = expected == "grey50" ? Theme.Chrome : expected;

            Assert.Contains($"[{expectedStyle}]{window}", chrome.StatusLine(120).Markup, StringComparison.Ordinal);
            Assert.Contains($"[{expectedStyle}]{window}", custom.StatusLine(120).Markup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Theory]
    [InlineData(false, null, "Work", "work")]
    [InlineData(true, null, "Plan", "plan")]
    [InlineData(false, "review", "review", "custom")]
    public void StatusLine_ShowsModeProviderAndShortSessionIdInCustomLine(
        bool plan,
        string? customMode,
        string label,
        string token)
    {
        Theme.Reset();
        var chrome = new ShellChrome
        {
            CustomStatusLine = true,
            StatusLineFields = ["mode", "provider", "session-id"],
            PlanMode = plan,
            CustomMode = customMode,
            Provider = "openai",
            SessionId = "0123456789abcdef"
        };
        var color = token switch { "plan" => Theme.Plan, "custom" => Theme.Custom, _ => Theme.Work };

        var line = chrome.StatusLine(120);

        Assert.Contains($"{label}  ·  openai  ·  01234567", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789", line.Plain, StringComparison.Ordinal);
        Assert.Contains($"[{color}]{label}", line.Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Chrome}]openai", line.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_ShowsReviewerFieldsAndOmitsTheOnesThatAreOff()
    {
        Theme.Reset();
        var chrome = new ShellChrome
        {
            CustomStatusLine = true,
            StatusLineFields = ["approval-model", "approval-tier", "extended-model", "extended-trigger", "extended-primary"],
            ReviewerFields = new Dictionary<string, string>
            {
                ["approval-model"] = "Reviewer judge",
                ["extended-model"] = "Extended deep",
                ["extended-trigger"] = "Extended Trigger Medium",
                ["extended-primary"] = "Extended Primary Off"
            }
        };

        var line = chrome.StatusLine(200);

        Assert.Equal(
            "Reviewer judge  ·  Extended deep  ·  Extended Trigger Medium  ·  Extended Primary Off",
            line.Plain.Trim());
    }

    [Fact]
    public void StatusLine_ColorsReviewerFieldsFromTheMatchingGaugeWithoutTheLabel()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["speed"] = [new GaugeRule(null, null, ["Fast"], null, "gold1")],
            ["model"] = [new GaugeRule(null, null, ["judge"], null, "red")]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);
        try
        {
            var chrome = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["extended-speed", "approval-model"],
                ReviewerFields = new Dictionary<string, string>
                {
                    ["extended-speed"] = "Extended Speed Fast",
                    ["approval-model"] = "Reviewer judge"
                }
            };

            var line = chrome.StatusLine(200);

            Assert.Contains("[gold1]Extended Speed Fast", line.Markup, StringComparison.Ordinal);
            Assert.Contains("[red]Reviewer judge", line.Markup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Fact]
    public void StatusLine_ReviewerFieldGaugeWinsOverTheSharedGauge()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["model"] = [new GaugeRule(null, null, ["judge"], null, "red")],
            ["extended-model"] = [new GaugeRule(null, null, ["judge"], null, "gold1")],
            ["extended-primary"] = [new GaugeRule(null, null, ["Off"], null, "yellow")],
            ["extended-trigger"] = [new GaugeRule(null, null, ["Medium"], null, "orange1")]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);
        try
        {
            var chrome = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["approval-model", "extended-model", "extended-trigger", "extended-primary"],
                ReviewerFields = new Dictionary<string, string>
                {
                    ["approval-model"] = "Reviewer judge",
                    ["extended-model"] = "Extended judge",
                    ["extended-trigger"] = "Extended Trigger Medium",
                    ["extended-primary"] = "Extended Primary Off"
                }
            };

            var markup = chrome.StatusLine(200).Markup;

            Assert.Contains("[red]Reviewer judge", markup, StringComparison.Ordinal);
            Assert.Contains("[gold1]Extended judge", markup, StringComparison.Ordinal);
            Assert.Contains("[orange1]Extended Trigger Medium", markup, StringComparison.Ordinal);
            Assert.Contains("[yellow]Extended Primary Off", markup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Fact]
    public void StatusLine_OmitsAnEmptyProviderAndSessionId()
    {
        var chrome = new ShellChrome
        {
            CustomStatusLine = true,
            StatusLineFields = ["provider", "session-id", "approval"],
            Approval = "Full"
        };

        var line = chrome.StatusLine(120);

        Assert.Equal("Full", line.Plain.Trim());
    }

    [Fact]
    public void StatusLine_ColorsModeAndProviderFromThemeRules()
    {
        var gauges = new Dictionary<string, IReadOnlyList<GaugeRule>>
        {
            ["mode"] = [new GaugeRule(null, null, ["plan"], null, "gold1")],
            ["provider"] = [new GaugeRule(null, null, [], "^open", "green")]
        };
        Assert.True(Theme.TryApply(new Dictionary<string, string>(), gauges, out var error), error);
        try
        {
            var chrome = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["mode", "provider"],
                PlanMode = true,
                Provider = "openai"
            };
            var work = new ShellChrome
            {
                CustomStatusLine = true,
                StatusLineFields = ["mode"],
                Provider = "openai"
            };

            Assert.Contains("[gold1]Plan", chrome.StatusLine(120).Markup, StringComparison.Ordinal);
            Assert.Contains("[green]openai", chrome.StatusLine(120).Markup, StringComparison.Ordinal);
            Assert.Contains($"[{Theme.Work}]Work", work.StatusLine(120).Markup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }

    [Fact]
    public void StatusLine_ModelContextWindowAndPromptSetKeepDefaultColorsWithoutRules()
    {
        Theme.Reset();
        var chrome = new ShellChrome { Model = "gpt-5", ContextWindow = "Context 1M", PromptSet = "Review" };

        var markup = chrome.StatusLine(120).Markup;

        Assert.Contains($"[{Theme.User}]gpt-5", markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Chrome}]Context 1M", markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Chrome}]Prompt Review", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLine_ThinkingStaysChromeWithoutThemeRules()
    {
        Theme.Reset();
        var chrome = new ShellChrome { Thinking = "Think High", ServiceTier = "Tier Priority", Speed = "Speed Fast" };

        var markup = chrome.StatusLine(120).Markup;

        Assert.Contains($"[{Theme.Chrome}]Think High", markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Chrome}]Tier Priority", markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Chrome}]Speed Fast", markup, StringComparison.Ordinal);
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
    public void StatusLine_ShowsNonDefaultPromptSet()
    {
        var chrome = new ShellChrome
        {
            Approval = "Default",
            PromptSet = "concise",
            Usage = "CTX --"
        };

        var line = chrome.StatusLine(120);

        Assert.Contains("Prompt concise", line.Plain, StringComparison.Ordinal);
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

    [Fact]
    public void ActivityAndProgress_AreReducedToOneCleanLine()
    {
        var chrome = new ShellChrome
        {
            Model = "m",
            Activity = "Bad\nTool\u001b[2J",
            Progress = "Calling Bad\nTool\u001b]0;x\u0007"
        };

        var status = chrome.StatusLine(120);
        var progress = chrome.ProgressLine(120);

        Assert.False(TerminalText.HasControls(status.Markup));
        Assert.False(TerminalText.HasControls(progress.Markup));
        Assert.Contains("• Bad Tool", status.Plain, StringComparison.Ordinal);
        Assert.Contains("Calling Bad Tool", progress.Plain, StringComparison.Ordinal);
    }
}
