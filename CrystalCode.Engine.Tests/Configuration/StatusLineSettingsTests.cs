using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class StatusLineSettingsTests
{
    [Fact]
    public void Fields_AcceptModeProviderAndSessionId()
    {
        var settings = new StatusLineSettings(true, ["mode", "provider", "session-id", "model"]);

        Assert.Equal(["mode", "provider", "session-id", "model"], settings.Fields);
    }

    [Fact]
    public void Fields_AcceptApprovalAndExtendedApprovalFields()
    {
        string[] fields =
        [
            "approval-provider", "approval-model", "approval-thinking", "approval-tier", "approval-speed",
            "approval-context",
            "extended-provider", "extended-model", "extended-thinking", "extended-tier", "extended-speed",
            "extended-context",
            "extended-trigger", "extended-primary"
        ];

        var settings = new StatusLineSettings(true, fields);

        Assert.Equal(fields, settings.Fields);
        Assert.All(fields, field => Assert.DoesNotContain(field, StatusLineSettings.DefaultFields));
    }

    [Fact]
    public void AvailableFields_DoesNotChangeTheDefaultFields()
    {
        Assert.DoesNotContain("mode", StatusLineSettings.DefaultFields);
        Assert.DoesNotContain("provider", StatusLineSettings.DefaultFields);
        Assert.DoesNotContain("session-id", StatusLineSettings.DefaultFields);
    }

    [Fact]
    public void Rules_AreKeptInFieldOrderAndDropOrderIsNormalized()
    {
        var settings = new StatusLineSettings(
            true,
            ["model", "elapsed", "workspace"],
            [new("workspace", HideBelow: 90), new("MODEL", Keep: true)],
            [" Elapsed ", "elapsed"]);

        Assert.Equal(["model", "workspace"], settings.Rules.Select(rule => rule.Field));
        Assert.Equal(["elapsed"], settings.DropOrder);
    }

    [Fact]
    public void Rules_RejectInvalidCombinations()
    {
        string[] fields = ["model", "elapsed"];

        Assert.Throws<ArgumentException>(() => new StatusLineSettings(true, fields, [new("model", true, 80)]));
        Assert.Throws<ArgumentException>(() => new StatusLineSettings(true, fields, [new("model", false, 0)]));
        Assert.Throws<ArgumentException>(() => new StatusLineSettings(true, fields, [new("tools", true)]));
        Assert.Throws<ArgumentException>(() => new StatusLineSettings(true, fields, [new("model", true), new("model", true)]));
        Assert.Throws<ArgumentException>(() => new StatusLineSettings(true, fields, null, ["tools"]));
        Assert.Throws<ArgumentException>(() => new StatusLineSettings(true, fields, [new("model", true)], ["model"]));
    }

    [Fact]
    public void WithFields_DropsRulesAndOrderForFieldsThatLeaveTheLine()
    {
        var settings = new StatusLineSettings(
            true, ["model", "elapsed"], [new("model", Keep: true)], ["elapsed"]);

        var changed = settings.WithFields(["elapsed", "tools"]);

        Assert.Empty(changed.Rules);
        Assert.Equal(["elapsed"], changed.DropOrder);
    }
}
