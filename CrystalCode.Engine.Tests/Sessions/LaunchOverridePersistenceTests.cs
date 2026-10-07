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

    [Fact]
    public async Task RunBaseline_PreferenceSaveLeavesLaunchOverridesOffDisk()
    {
        var live = HeadlessSession.ScriptedSettings(thinking: true)
            .WithApproval(ApprovalMode.Audit)
            .WithThinkingEffort(new ThinkingSelection("high"));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: live,
            persistedSettings: HarnessSettings.CreateDefault());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tokens on", CancellationToken.None);

        var saved = Read(headless);
        Assert.Equal("deepseek", saved.Provider);
        Assert.Equal("deepseek-flash", saved.Model);
        Assert.Equal("default", saved.Approval);
        Assert.Null(saved.ThinkingEffort);
        Assert.True(saved.EstimatedTokens);
        Assert.DoesNotContain("audit", File.ReadAllText(headless.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreferenceSave_KeepsLaunchApprovalGearOffDisk()
    {
        var baseline = HeadlessSession.ScriptedSettings(thinking: true)
            .WithApprovalModel(new ApprovalModelSettings(
                true,
                "scripted",
                "model",
                ThinkingSelection.Parse("low")));
        var live = baseline.WithApprovalModel(
            baseline.ApprovalModel.WithThinkingEffort(ThinkingSelection.Parse("high")));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: live,
            persistedSettings: baseline);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tokens on", CancellationToken.None);

        var saved = Read(headless);
        Assert.NotNull(saved.ApprovalModel);
        Assert.True(saved.ApprovalModel!.Enabled);
        Assert.Equal("low", saved.ApprovalModel.ThinkingEffort);
    }

    private static SettingsDocument Read(HeadlessSession headless)
    {
        var json = File.ReadAllText(headless.Home.ConfigPath);
        return JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? throw new InvalidOperationException("Settings file was empty.");
    }
}
