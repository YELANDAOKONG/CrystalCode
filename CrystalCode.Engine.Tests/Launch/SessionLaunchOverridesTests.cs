using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Launch;

public sealed class SessionLaunchOverridesTests
{
    [Fact]
    public void Overrides_ApplyOnlyInMemory()
    {
        var current = HarnessSettings.CreateDefault();
        var request = new SessionLaunchOptions
        {
            ModelCalls = "unlimited",
            ToolCalls = "0",
            Duration = "unlimited",
            BashTimeout = "unlimited",
            Skills = "off",
            ExternalTools = "off",
            Thinking = "max",
            Approval = "audit",
            PromptSet = "concise",
            PromptAttachments = "off"
        };

        var applied = AssertApplied(current, request);

        Assert.Null(applied.ExecutionBudget.MaximumModelCalls);
        Assert.Equal(0, applied.ExecutionBudget.MaximumToolCalls);
        Assert.Null(applied.ExecutionBudget.MaximumDuration);
        Assert.Null(applied.BashTimeoutSeconds);
        Assert.False(applied.Skills);
        Assert.False(applied.ExternalTools);
        Assert.Equal("max", applied.ThinkingEffort.Value);
        Assert.Equal(ApprovalMode.Audit, applied.Approval);
        Assert.Equal("concise", applied.PromptSetOverride);
        Assert.Equal(HarnessSettings.DefaultPromptSet, applied.PromptSet);
        Assert.False(applied.UsePromptAttachments);
        Assert.Null(current.PromptSetOverride);
        Assert.True(current.UsePromptAttachments);
        Assert.Equal(current.Provider, applied.Provider);
        Assert.Equal(TurnLimits.DefaultMaximumModelCalls, current.ExecutionBudget.MaximumModelCalls);
    }

    [Fact]
    public void AgentLimitOverrides_ApplyOnlyInMemoryAndKeepOtherFields()
    {
        var current = HarnessSettings.CreateDefault().WithAgents(
            new AgentSettings { Limits = new AgentLimits(maximumToolCalls: 40) });

        var applied = AssertApplied(current, new SessionLaunchOptions
        {
            AgentsModelCalls = "200",
            AgentsDuration = "3600"
        });

        Assert.Equal(new AgentLimits(200, 40, 3600), applied.Agents.Limits);
        Assert.Equal(new AgentLimits(maximumToolCalls: 40), current.Agents.Limits);

        var cleared = AssertApplied(applied, new SessionLaunchOptions { AgentsToolCalls = "unlimited" });
        Assert.Equal(new AgentLimits(200, null, 3600), cleared.Agents.Limits);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { AgentsModelCalls = "0" },
            out _,
            out _,
            out var error));
        Assert.Contains("Agent model calls", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesOverride_ChangesOnlyTheNamedSide()
    {
        var current = HarnessSettings.CreateDefault()
            .WithRuleFiles(RuleFileSettings.Default.WithGlobal(false));

        var applied = AssertApplied(current, new SessionLaunchOptions { Rules = "project=off" });

        Assert.False(applied.RuleFiles.Global);
        Assert.False(applied.RuleFiles.Project);
        Assert.False(current.RuleFiles.Global);
        Assert.True(current.RuleFiles.Project);
        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { Rules = "project=false" },
            out _,
            out _,
            out var error));
        Assert.Contains("--rules", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptAttachmentOverride_StaysInMemoryAndRejectsOff()
    {
        var current = HarnessSettings.CreateDefault();
        var applied = AssertApplied(
            current,
            new SessionLaunchOptions { PromptAttachment = [" beta ", "beta", "alpha"] });

        Assert.Equal(["beta", "alpha"], applied.PromptAttachments);
        Assert.Empty(current.PromptAttachments);
        Assert.True(applied.UsePromptAttachments);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions
            {
                PromptAttachments = "off",
                PromptAttachment = ["alpha"]
            },
            out _,
            out _,
            out var offError));
        Assert.Contains("--prompt-attachments is off", offError, StringComparison.Ordinal);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { PromptAttachment = ["Bad_Name"] },
            out _,
            out _,
            out var invalidError));
        Assert.Contains("invalid", invalidError, StringComparison.Ordinal);
    }

    [Fact]
    public void Overrides_ConfiguresTheApprovalModelInMemory()
    {
        var stored = HarnessSettings.CreateDefault().WithApprovalModel(
            new ApprovalModelSettings(true, "openai", "gpt-5.6-sol"));

        var off = AssertApplied(stored, new SessionLaunchOptions { ApprovalModel = "off" });

        Assert.False(off.ApprovalModel.Enabled);
        Assert.Equal("openai", off.ApprovalModel.Provider);
        Assert.Equal("gpt-5.6-sol", off.ApprovalModel.Model);
        Assert.True(stored.ApprovalModel.Enabled);

        var on = AssertApplied(
            stored.WithApprovalModel(stored.ApprovalModel.DisabledCopy()),
            new SessionLaunchOptions { ApprovalModel = "on" });
        Assert.True(on.ApprovalModel.Enabled);
        Assert.Equal("gpt-5.6-sol", on.ApprovalModel.Model);

        var selected = AssertApplied(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions
            {
                ApprovalProvider = "openai",
                ApprovalModelId = "gpt-5.6-terra"
            });
        Assert.True(selected.ApprovalModel.Enabled);
        Assert.Equal("openai", selected.ApprovalModel.Provider);
        Assert.Equal("gpt-5.6-terra", selected.ApprovalModel.Model);
        Assert.Equal(ProviderName.DeepSeek, selected.Provider);

        var geared = AssertApplied(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions { ApprovalThinking = "high" });
        Assert.False(geared.ApprovalModel.Enabled);
        Assert.Equal("high", geared.ApprovalModel.ThinkingEffort.Value);

        var selectedWithGear = AssertApplied(
            stored,
            new SessionLaunchOptions
            {
                ApprovalModelId = "gpt-5.6-sol",
                ApprovalThinking = "high"
            });
        Assert.True(selectedWithGear.ApprovalModel.Enabled);
        Assert.Equal("gpt-5.6-sol", selectedWithGear.ApprovalModel.Model);
        Assert.Equal("high", selectedWithGear.ApprovalModel.ThinkingEffort.Value);
        Assert.Equal(ThinkingSelection.Default, stored.ApprovalModel.ThinkingEffort);

        var storedWithGear = stored.WithApprovalModel(
            stored.ApprovalModel.WithThinkingEffort(ThinkingSelection.Parse("high")));

        var onKeepsGear = AssertApplied(
            storedWithGear,
            new SessionLaunchOptions { ApprovalModel = "on" });
        Assert.True(onKeepsGear.ApprovalModel.Enabled);
        Assert.Equal("high", onKeepsGear.ApprovalModel.ThinkingEffort.Value);

        var selectedKeepsGear = AssertApplied(
            storedWithGear,
            new SessionLaunchOptions { ApprovalModelId = "gpt-5.6-sol" });
        Assert.True(selectedKeepsGear.ApprovalModel.Enabled);
        Assert.Equal("gpt-5.6-sol", selectedKeepsGear.ApprovalModel.Model);
        Assert.Equal("high", selectedKeepsGear.ApprovalModel.ThinkingEffort.Value);

        var offKeepsGear = AssertApplied(
            storedWithGear,
            new SessionLaunchOptions { ApprovalModel = "off" });
        Assert.False(offKeepsGear.ApprovalModel.Enabled);
        Assert.Equal("high", offKeepsGear.ApprovalModel.ThinkingEffort.Value);
    }

    [Fact]
    public void Overrides_ConfiguresTheCompactionModelInMemory()
    {
        var stored = HarnessSettings.CreateDefault().WithCompactionModel(
            new CompactionModelSettings(true, "openai", "gpt-5.6-sol"));

        var off = AssertApplied(stored, new SessionLaunchOptions { CompactionModel = "off" });

        Assert.False(off.CompactionModel.Enabled);
        Assert.Equal("openai", off.CompactionModel.Provider);
        Assert.Equal("gpt-5.6-sol", off.CompactionModel.Model);
        Assert.True(stored.CompactionModel.Enabled);

        var on = AssertApplied(
            stored.WithCompactionModel(stored.CompactionModel.DisabledCopy()),
            new SessionLaunchOptions { CompactionModel = "on" });
        Assert.True(on.CompactionModel.Enabled);
        Assert.Equal("gpt-5.6-sol", on.CompactionModel.Model);

        var selected = AssertApplied(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions
            {
                CompactionProvider = "openai",
                CompactionModelId = "gpt-5.6-terra"
            });
        Assert.True(selected.CompactionModel.Enabled);
        Assert.Equal("openai", selected.CompactionModel.Provider);
        Assert.Equal("gpt-5.6-terra", selected.CompactionModel.Model);
        Assert.Equal(ProviderName.DeepSeek, selected.Provider);

        var geared = AssertApplied(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions { CompactionThinking = "off" });
        Assert.False(geared.CompactionModel.Enabled);
        Assert.Equal(ThinkingSelection.Off, geared.CompactionModel.ThinkingEffort);

        var slotted = AssertApplied(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions { CompactionTier = "priority", CompactionSpeed = "fast" });
        Assert.Equal("priority", slotted.CompactionModel.ServiceTier);
        Assert.Equal("fast", slotted.CompactionModel.Speed);

        Assert.False(SessionLaunchOverrides.TryApply(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions { CompactionModel = "on" },
            out _,
            out _,
            out var missing));
        Assert.Equal("Pass --compaction-model-id.", missing);

        Assert.False(SessionLaunchOverrides.TryApply(
            stored,
            new SessionLaunchOptions { CompactionModel = "off", CompactionProvider = "openai" },
            out _,
            out _,
            out var combined));
        Assert.Equal(
            "Do not pass --compaction-provider or --compaction-model-id when --compaction-model is off.",
            combined);
    }

    [Fact]
    public void Overrides_RejectsApprovalModelConflicts()
    {
        var current = HarnessSettings.CreateDefault();

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ApprovalModel = "off", ApprovalProvider = "openai" },
            out _,
            out _,
            out var combined));
        Assert.Equal(
            "Do not pass --approval-provider or --approval-model-id when --approval-model is off.",
            combined);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ApprovalModel = "on" },
            out _,
            out _,
            out var missing));
        Assert.Equal("Pass --approval-model-id.", missing);
        Assert.False(current.ApprovalModel.Enabled);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ApprovalThinking = "bad effort" },
            out _,
            out _,
            out var gearError));
        Assert.Contains("Thinking effort must be", gearError, StringComparison.Ordinal);
    }

    [Fact]
    public void Overrides_RejectsAnInvalidQuotaOrModePair()
    {
        var current = HarnessSettings.CreateDefault();

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ModelCalls = "0" },
            out _,
            out _,
            out var quotaError));
        Assert.Contains("positive", quotaError, StringComparison.OrdinalIgnoreCase);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { Plan = true, Work = true },
            out _,
            out _,
            out var modeError));
        Assert.Contains("--plan", modeError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "audit")]
    [InlineData(false, true, "audit")]
    [InlineData(true, true, null)]
    public void Overrides_RejectsMoreThanOneModeFlag(bool plan, bool work, string? mode)
    {
        Assert.False(SessionLaunchOverrides.TryApply(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions { Plan = plan, Work = work, Mode = mode },
            out _,
            out _,
            out var error));
        Assert.Contains("--mode", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, null, "plan")]
    [InlineData(false, true, null, "work")]
    [InlineData(false, false, " audit ", "audit")]
    [InlineData(false, false, null, null)]
    public void Overrides_ReportsTheRequestedMode(bool plan, bool work, string? requested, string? expected)
    {
        Assert.True(SessionLaunchOverrides.TryApply(
            HarnessSettings.CreateDefault(),
            new SessionLaunchOptions { Plan = plan, Work = work, Mode = requested },
            out _,
            out var mode,
            out _));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void Overrides_AppliesDisabledBuiltInToolsWithoutSavingThem()
    {
        var current = HarnessSettings.CreateDefault();
        var applied = AssertApplied(
            current,
            new SessionLaunchOptions { DisableTool = [" read ", "bash", "read"] });

        Assert.Equal(["read", "bash"], applied.DisabledBuiltInTools.Plan);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { DisableTool = ["deploy"] },
            out _,
            out _,
            out var error));
        Assert.Contains("Unknown built-in tool", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Overrides_RejectsAnUnknownProviderOrModel()
    {
        var current = HarnessSettings.CreateDefault();

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { Provider = "nosuchprovider" },
            out _,
            out _,
            out var providerError));
        Assert.Contains("nosuchprovider", providerError, StringComparison.Ordinal);
        Assert.Contains("not configured", providerError, StringComparison.Ordinal);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { Model = "nosuch-model" },
            out _,
            out _,
            out var modelError));
        Assert.Contains("nosuch-model", modelError, StringComparison.Ordinal);
        Assert.Equal(ProviderName.DeepSeek, current.Provider);
        Assert.Equal("deepseek-flash", current.Model);
    }

    [Fact]
    public void Overrides_SelectsADeclaredContextBudgetInMemory()
    {
        var current = TieredContextSettings();
        var applied = AssertApplied(current, new SessionLaunchOptions { Context = "1m" });
        Assert.Equal(1_000_000, applied.ContextWindow);
        Assert.Equal(272_000, current.ContextWindow);
        Assert.Empty(current.ContextSelections);

        var exact = AssertApplied(current, new SessionLaunchOptions { Context = "272000" });
        Assert.Equal(272_000, exact.ContextWindow);

        var other = applied.WithSelection(applied.Provider, "small");
        Assert.Equal(128_000, other.ContextWindow);
        Assert.Equal(1_000_000, other.WithSelection(applied.Provider, "large").ContextWindow);

        Assert.False(SessionLaunchOverrides.TryApply(
            current.WithSelection(current.Provider, "small"),
            new SessionLaunchOptions { Context = "128000" },
            out _,
            out _,
            out var single));
        Assert.Equal("The selected model has one context budget.", single);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { Context = "128k" },
            out _,
            out _,
            out var unavailable));
        Assert.Contains("not available", unavailable, StringComparison.Ordinal);
        Assert.Contains("272K", unavailable, StringComparison.Ordinal);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { Context = " " },
            out _,
            out _,
            out var blank));
        Assert.Contains("Context budget is required.", blank, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableThinking_SkipsOffWhenTheModelDoesNotThink()
    {
        var settings = HarnessSettings.CreateDefault()
            .WithOverrides("openai", "gpt-5.6-sol")
            .WithThinkingEffort(ThinkingSelection.Off);

        Assert.Empty(SessionLaunchOverrides.UnavailableThinkingNotes(settings));

        var named = settings.WithThinkingEffort(ThinkingSelection.Parse("high"));
        Assert.Contains(
            "Thinking effort 'high' is not available",
            Assert.Single(SessionLaunchOverrides.UnavailableThinkingNotes(named)),
            StringComparison.Ordinal);
    }

    private static HarnessSettings AssertApplied(HarnessSettings current, SessionLaunchOptions request)
    {
        Assert.True(SessionLaunchOverrides.TryApply(
            current,
            request,
            out var applied,
            out var mode,
            out var error));
        Assert.Null(mode);
        Assert.Equal(string.Empty, error);
        return applied;
    }

    [Fact]
    public void Overrides_SetExtendedApprovalForThisProcess()
    {
        var current = SlotSettings();

        var applied = AssertApplied(
            current,
            new SessionLaunchOptions
            {
                ExtendedApprovalModelId = "judge",
                ExtendedApprovalThinking = "high",
                ExtendedApprovalSpeed = "Fast",
                ExtendedApprovalTrigger = "medium",
                ExtendedApprovalPrimary = "off",
                ApprovalModelId = "judge",
                ApprovalTier = "flex"
            });

        Assert.True(applied.ExtendedApproval.Enabled);
        Assert.Equal("slots", applied.ExtendedApproval.Reviewer.Provider);
        Assert.Equal("judge", applied.ExtendedApproval.Reviewer.Model);
        Assert.Equal("high", applied.ExtendedApproval.Reviewer.ThinkingEffort.Value);
        Assert.Equal("fast", applied.ExtendedApproval.Reviewer.Speed);
        Assert.Equal(ReviewRiskLevel.Medium, applied.ExtendedApproval.Trigger);
        Assert.False(applied.ExtendedApproval.IncludePrimaryReview);
        Assert.Equal("flex", applied.ApprovalModel.ServiceTier);
        Assert.False(current.ExtendedApproval.Enabled);

        var off = AssertApplied(applied, new SessionLaunchOptions { ExtendedApproval = "off" });
        Assert.False(off.ExtendedApproval.Enabled);
        Assert.Equal("judge", off.ExtendedApproval.Reviewer.Model);
    }

    [Fact]
    public void Overrides_RejectExtendedApprovalConflictsAndBadValues()
    {
        var current = SlotSettings();

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ExtendedApproval = "off", ExtendedApprovalModelId = "judge" },
            out _,
            out _,
            out var combined));
        Assert.Equal(
            "Do not pass --extended-approval-provider or --extended-approval-model-id when --extended-approval is off.",
            combined);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ExtendedApproval = "on" },
            out _,
            out _,
            out var missing));
        Assert.Equal("Pass --extended-approval-model-id.", missing);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ExtendedApprovalModelId = "judge", ExtendedApprovalTrigger = "severe" },
            out _,
            out _,
            out var trigger));
        Assert.Equal("--extended-approval-trigger must be low, medium, or high.", trigger);

        Assert.False(SessionLaunchOverrides.TryApply(
            current,
            new SessionLaunchOptions { ExtendedApprovalModelId = "plain", ExtendedApprovalSpeed = "fast" },
            out _,
            out _,
            out var unlisted));
        Assert.Contains("Extended approval model speed is not available", unlisted, StringComparison.Ordinal);
    }

    private static HarnessSettings SlotSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("slots"),
            ProviderProtocol.Anthropic,
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings>
            {
                ["plain"] = new(200_000, thinking: true, thinkingEfforts: ["high"]),
                ["judge"] = new(200_000, thinking: true, thinkingEfforts: ["high"], serviceTiers: ["flex"], speeds: ["fast"])
            },
            requiresApiKey: false);
        return new HarnessSettings(
            provider.Name,
            "plain",
            ApprovalMode.Review,
            0.8,
            new ProviderCatalog([provider]));
    }

    private static HarnessSettings TieredContextSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("local"),
            ProviderProtocol.Ollama,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["large"] = new(1_000_000, contextWindows: [272_000, 1_000_000]),
                ["small"] = new(128_000)
            },
            requiresApiKey: false);
        return new HarnessSettings(
            provider.Name,
            "large",
            ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            ProviderCatalog.CreateStarter().Overlay([provider]));
    }
}
