using System.Text.Json;

using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class LaunchOverridePersistenceTests
{
    [Fact]
    public async Task PreferenceSave_KeepsLaunchApprovalAndThinkingOffDisk()
    {
        var baseline = HeadlessSession.ScriptedSettings(thinking: true);
        var live = baseline
            .WithApproval(ApprovalMode.Audit)
            .WithThinkingEffort(new ThinkingSelection("high"));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: live,
            persistedSettings: baseline.WithOverrides("scripted", "model"));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tokens on", CancellationToken.None);
        var afterTokens = Read(headless);
        Assert.Equal("default", afterTokens.Approval);
        Assert.Null(afterTokens.ThinkingEffort);
        Assert.True(afterTokens.EstimatedTokens);
        Assert.Equal("scripted", afterTokens.Provider);

        await headless.Session.SubmitAsync("/thinking low", CancellationToken.None);
        var afterThinking = Read(headless);
        Assert.Equal("default", afterThinking.Approval);
        Assert.Equal("low", afterThinking.ThinkingEffort);

        await headless.Session.SubmitAsync("/approval review", CancellationToken.None);
        var afterApproval = Read(headless);
        Assert.Equal("review", afterApproval.Approval);
        Assert.Equal("low", afterApproval.ThinkingEffort);
        Assert.DoesNotContain("audit", File.ReadAllText(headless.Home.ConfigPath), StringComparison.Ordinal);
    }

    private static SettingsDocument Read(HeadlessSession headless)
    {
        var json = File.ReadAllText(headless.Home.ConfigPath);
        return JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? throw new InvalidOperationException("Settings file was empty.");
    }
}
