using System.Text.Json;

using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
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

    [Fact]
    public async Task ApprovalModelCommand_KeepsLaunchGearOffDiskAndStoredGearOnDisk()
    {
        var persisted = HeadlessSession.ScriptedSettings(thinking: true)
            .WithApprovalModel(new ApprovalModelSettings(
                true,
                "scripted",
                "model",
                ThinkingSelection.Parse("low")));
        var live = persisted.WithApprovalModel(
            persisted.ApprovalModel.WithThinkingEffort(ThinkingSelection.Parse("high")));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: live,
            persistedSettings: persisted);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/approval model model", CancellationToken.None);

        var selected = Read(headless);
        Assert.NotNull(selected.ApprovalModel);
        Assert.Equal("low", selected.ApprovalModel!.ThinkingEffort);

        headless.Observer.Clear();
        var quit = await headless.Session.SubmitAsync("/status", CancellationToken.None);
        Assert.False(quit);
        var status = headless.Observer.Events.OfType<StatusReported>().Single().Status;
        Assert.Equal("Think High", status.ApprovalThinking);

        await headless.Session.SubmitAsync("/approval model off", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval model on", CancellationToken.None);

        var toggled = Read(headless);
        Assert.NotNull(toggled.ApprovalModel);
        Assert.True(toggled.ApprovalModel!.Enabled);
        Assert.Equal("low", toggled.ApprovalModel.ThinkingEffort);
    }

    [Fact]
    public async Task PreferenceSave_KeepsLaunchContextBudgetOffDisk()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>
            {
                ["model"] = new(1_000_000, contextWindows: [272_000, 1_000_000]),
                ["other"] = new(128_000)
            });
        var baseline = new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: baseline.WithContextBudget("1m"),
            persistedSettings: baseline);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tokens on", CancellationToken.None);

        var saved = Read(headless);
        Assert.Null(saved.ContextSelections);
        Assert.True(saved.EstimatedTokens);

        await headless.Session.SubmitAsync("/context 272k", CancellationToken.None);

        using var written = JsonDocument.Parse(File.ReadAllText(headless.Home.ConfigPath));
        Assert.Equal(
            272_000,
            written.RootElement
                .GetProperty("contextSelections")
                .GetProperty("scripted")
                .GetProperty("model")
                .GetInt32());
    }

    private static SettingsDocument Read(HeadlessSession headless)
    {
        var json = File.ReadAllText(headless.Home.ConfigPath);
        return JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? throw new InvalidOperationException("Settings file was empty.");
    }
}
