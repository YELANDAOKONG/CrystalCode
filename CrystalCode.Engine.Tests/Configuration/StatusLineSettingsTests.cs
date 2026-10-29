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
    public void AvailableFields_DoesNotChangeTheDefaultFields()
    {
        Assert.DoesNotContain("mode", StatusLineSettings.DefaultFields);
        Assert.DoesNotContain("provider", StatusLineSettings.DefaultFields);
        Assert.DoesNotContain("session-id", StatusLineSettings.DefaultFields);
    }
}
