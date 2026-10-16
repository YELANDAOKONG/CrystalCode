using Crystal.Reasoning;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class HarnessSettingsTests
{
    [Fact]
    public void WithSelection_SwitchesProviderAndModel()
    {
        var catalog = ModelSelectionTests.Catalog();
        var settings = new HarnessSettings(
            ProviderName.DeepSeek,
            "deepseek-v4-flash",
            ApprovalMode.Default,
            0.8,
            catalog);

        var next = settings.WithSelection(new ProviderName("openrouter"), "anthropic/claude-sonnet-4");

        Assert.Equal("openrouter", next.Provider.Value);
        Assert.Equal("anthropic/claude-sonnet-4", next.Model);
        Assert.Equal(200000, next.ActiveModel.ContextWindow);
        Assert.Equal(ProviderName.DeepSeek, settings.Provider);
    }

    [Fact]
    public void WithEstimatedTokens_SetsHostFlag()
    {
        var catalog = ModelSelectionTests.Catalog();
        var settings = new HarnessSettings(
            ProviderName.DeepSeek,
            "deepseek-v4-flash",
            ApprovalMode.Default,
            0.8,
            catalog);

        var next = settings.WithEstimatedTokens(true);

        Assert.True(next.EstimatedTokens);
        Assert.False(settings.EstimatedTokens);
    }

    [Fact]
    public void WithVerboseTools_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithVerboseTools(false);

        Assert.False(next.VerboseTools);
        Assert.True(settings.VerboseTools);
    }

    [Fact]
    public void WithVerboseApprovals_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithVerboseApprovals(false);

        Assert.False(next.VerboseApprovals);
        Assert.True(settings.VerboseApprovals);
        Assert.True(next.VerboseTools);
        Assert.True(next.VerboseReview);
    }

    [Fact]
    public void WithVerboseReview_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithVerboseReview(false);

        Assert.False(next.VerboseReview);
        Assert.True(settings.VerboseReview);
        Assert.True(next.VerboseApprovals);
    }

    [Fact]
    public void WithVerboseResults_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithVerboseResults(false);

        Assert.False(next.VerboseResults);
        Assert.True(settings.VerboseResults);
        Assert.True(next.VerboseTools);
        Assert.True(next.VerboseCommands);
    }

    [Fact]
    public void WithVerboseCalls_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithVerboseCalls(false);

        Assert.False(next.VerboseCalls);
        Assert.True(settings.VerboseCalls);
        Assert.True(next.VerboseResults);
    }

    [Fact]
    public void WithShowCompactionSummary_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithShowCompactionSummary(false);

        Assert.False(next.ShowCompactionSummary);
        Assert.True(settings.ShowCompactionSummary);
        Assert.True(next.VerboseThinking);
    }

    [Fact]
    public void WithShowAutomaticCompactionSummary_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithShowAutomaticCompactionSummary(true);

        Assert.True(next.ShowAutomaticCompactionSummary);
        Assert.False(settings.ShowAutomaticCompactionSummary);
        Assert.True(next.ShowCompactionSummary);
    }

    [Fact]
    public void WithVerboseThinking_SetsHostFlag()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithVerboseThinking(false);

        Assert.False(next.VerboseThinking);
        Assert.True(settings.VerboseThinking);
        Assert.True(next.VerboseApprovals);
    }

    [Fact]
    public void WithPromptSet_SetsGlobalSelection()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithPromptSet("concise");

        Assert.Equal("concise", next.PromptSet);
        Assert.Equal(HarnessSettings.DefaultPromptSet, settings.PromptSet);
    }

    [Fact]
    public void WithPromptSetOverride_StaysInMemory()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithPromptSetOverride(" concise ");

        Assert.Equal("concise", next.PromptSetOverride);
        Assert.Null(settings.PromptSetOverride);
        Assert.Equal(HarnessSettings.DefaultPromptSet, next.PromptSet);
    }

    [Fact]
    public void WithUsePromptAttachments_StaysInMemory()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithUsePromptAttachments(false);

        Assert.False(next.UsePromptAttachments);
        Assert.True(settings.UsePromptAttachments);
    }

    [Fact]
    public void WithPromptAttachments_ReplacesTheEnabledList()
    {
        var settings = HarnessSettings.CreateDefault().WithPromptAttachments(["alpha"]);

        var next = settings.WithPromptAttachments(["beta", " gamma "]);

        Assert.Equal(["beta", "gamma"], next.PromptAttachments);
        Assert.Equal(["alpha"], settings.PromptAttachments);
    }

    [Fact]
    public void WithExportDirectory_ClearsConfiguredDirectory()
    {
        var settings = HarnessSettings.CreateDefault().WithExportDirectory("workspace");

        var cleared = settings.WithExportDirectory(null);

        Assert.Equal("workspace", settings.ExportDirectory);
        Assert.Null(cleared.ExportDirectory);
    }

    [Fact]
    public void WithStatusLine_EnablesOrderedFields()
    {
        var settings = HarnessSettings.CreateDefault();

        var next = settings.WithStatusLine(
            new StatusLineSettings(true, ["context-left", "session-total"]));

        Assert.True(next.StatusLine.Enabled);
        Assert.Equal(["context-left", "session-total"], next.StatusLine.Fields);
        Assert.False(settings.StatusLine.Enabled);
    }

    [Fact]
    public void WithBashTimeout_PreservesUnlimitedWhenOtherSettingsChange()
    {
        var settings = HarnessSettings.CreateDefault().WithBashTimeout(null);

        var next = settings.WithVerboseTools(false);

        Assert.Null(settings.BashTimeoutSeconds);
        Assert.Null(next.BashTimeoutSeconds);
        Assert.False(next.VerboseTools);
    }

    [Fact]
    public void WithBashTimeout_RejectsUnsupportedSeconds()
    {
        var settings = HarnessSettings.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.WithBashTimeout(0));
    }

    [Fact]
    public void CreateDefault_UsesBashTimeoutDefaults()
    {
        var settings = HarnessSettings.CreateDefault();

        Assert.Equal(600, settings.BashTimeoutSeconds);
        Assert.Equal(10800, settings.BackgroundBashTimeoutSeconds);
    }

    [Fact]
    public void WithBackgroundBashTimeout_PreservesUnlimitedWhenOtherSettingsChange()
    {
        var settings = HarnessSettings.CreateDefault().WithBackgroundBashTimeout(null);

        var next = settings.WithVerboseTools(false);

        Assert.Null(settings.BackgroundBashTimeoutSeconds);
        Assert.Null(next.BackgroundBashTimeoutSeconds);
        Assert.False(next.VerboseTools);
        Assert.Equal(600, next.BashTimeoutSeconds);
    }

    [Fact]
    public void WithBackgroundBashTimeout_RejectsUnsupportedSeconds()
    {
        var settings = HarnessSettings.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => settings.WithBackgroundBashTimeout(0));
    }

    [Fact]
    public void ApprovalModel_RejectsAnEnabledUnknownModelAndKeepsADisabledOne()
    {
        var settings = HarnessSettings.CreateDefault();

        Assert.False(settings.ApprovalModel.Enabled);
        Assert.Throws<ArgumentException>(() => new ApprovalModelSettings(true, "openai", " "));

        var disabled = settings.WithApprovalModel(
            new ApprovalModelSettings(false, "openai", "not-a-model"));

        Assert.False(disabled.ApprovalModel.Enabled);
        Assert.Equal("not-a-model", disabled.ApprovalModel.Model);
        Assert.False(settings.ApprovalModel.HasSelection);

        var enabled = Assert.Throws<InvalidOperationException>(() =>
            settings.WithApprovalModel(new ApprovalModelSettings(true, "openai", "not-a-model")));
        Assert.Contains("not-a-model", enabled.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageModel_MapsThinkingAndRejectsAModelThatCannotSeeImages()
    {
        var settings = HarnessSettings.CreateDefault();
        Assert.False(settings.ImageModel.IsConfigured);

        var geared = settings.WithImageModel(
            new ImageModelSettings("deepseek", "deepseek-flash", ThinkingSelection.Parse("high")));
        var model = geared.Catalog.GetModel(new ProviderName("deepseek"), "deepseek-flash");
        var reasoning = geared.ImageModel.ThinkingEffort.ToReasoningOptions(model);

        Assert.NotNull(reasoning);
        Assert.Equal(ReasoningMode.Enabled, reasoning.Mode);
        Assert.Equal("high", reasoning.Effort!.Value);

        var textOnly = Assert.Throws<InvalidOperationException>(() =>
            settings.WithImageModel(new ImageModelSettings("deepseek", "deepseek-v4-pro")));
        Assert.Contains("does not accept image input", textOnly.Message, StringComparison.Ordinal);
        Assert.False(settings.ImageModel.IsConfigured);
    }
}
