using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Plugins.Models;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginModelFactsTests
{
    [Fact]
    public void DescribeReview_WhenDisabled_HidesTheStoredSelection()
    {
        var settings = HarnessSettings.CreateDefault();
        var stored = new ApprovalModelSettings(false, "deepseek", "not-a-listed-model");

        var review = PluginModelFacts.DescribeReview(stored, settings.Catalog);

        Assert.False(review.Independent);
        Assert.Null(review.Model);
    }

    [Fact]
    public void DescribeReview_WhenEnabled_UsesTheReviewModelAndDefaultGear()
    {
        var settings = HarnessSettings.CreateDefault();
        var enabled = new ApprovalModelSettings(
            true,
            settings.Provider.Value,
            settings.Model);

        var session = PluginModelFacts.Describe(
            settings.ActiveProvider,
            settings.Model,
            settings.ActiveModel,
            "high");
        var review = PluginModelFacts.DescribeReview(enabled, settings.Catalog);

        Assert.Equal(settings.Provider.Value, session.Provider);
        Assert.Equal(settings.ActiveProvider.Protocol.Value, session.Protocol);
        Assert.Equal(settings.Model, session.Name);
        Assert.Equal(settings.ActiveModel.ContextWindow, session.ContextWindow);
        Assert.Equal(settings.ActiveModel.MaxTokens, session.MaxTokens);
        Assert.Equal(settings.ActiveModel.ImageInput, session.ImageInput);
        Assert.Equal(settings.ActiveModel.AudioInput, session.AudioInput);
        Assert.Equal(settings.ActiveModel.VideoInput, session.VideoInput);
        Assert.Equal("high", session.Thinking);
        Assert.True(review.Independent);
        Assert.NotNull(review.Model);
        Assert.Equal(settings.Model, review.Model.Name);
        Assert.Equal("default", review.Model.Thinking);
    }

    [Fact]
    public void DescribeReview_UsesTheStoredThinkingGear()
    {
        var settings = HarnessSettings.CreateDefault();
        var enabled = new ApprovalModelSettings(
            true,
            settings.Provider.Value,
            settings.Model,
            ThinkingSelection.Parse("high"));

        var review = PluginModelFacts.DescribeReview(enabled, settings.Catalog);

        Assert.True(review.Independent);
        Assert.NotNull(review.Model);
        Assert.Equal("high", review.Model.Thinking);
    }

    [Fact]
    public void Describe_PublishesDeclaredAudioAndVideoWithoutImageInput()
    {
        var providerName = new ProviderName("local");
        var model = new ModelSettings(8192, audioInput: true, videoInput: true);
        var provider = new ProviderDefinition(
            providerName,
            ProviderProtocol.OpenAI,
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["heard"] = model });
        var settings = new HarnessSettings(
            providerName,
            "heard",
            ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            new ProviderCatalog([provider]));

        var described = PluginModelFacts.Describe(provider, "heard", model, "default");
        var request = PluginClientRequests.From(settings, "not-a-key");

        Assert.False(described.ImageInput);
        Assert.True(described.AudioInput);
        Assert.True(described.VideoInput);
        Assert.False(request.ImageInput);
        Assert.True(request.AudioInput);
        Assert.True(request.VideoInput);
        Assert.False(PluginModel.Empty.AudioInput);
        Assert.False(PluginModel.Empty.VideoInput);
    }

    [Fact]
    public void View_RereadsTheCurrentSwitchAndGear()
    {
        var settings = HarnessSettings.CreateDefault();
        var effort = "low";
        var approval = new ApprovalModelSettings(false, settings.Provider.Value, settings.Model);
        var view = new PluginModelView(
            () => PluginModelFacts.Describe(
                settings.ActiveProvider,
                settings.Model,
                settings.ActiveModel,
                effort),
            () => PluginModelFacts.DescribeReview(approval, settings.Catalog));

        Assert.Equal("low", view.Session.Thinking);
        Assert.False(view.Review.Independent);

        effort = "high";
        approval = approval.EnabledCopy();

        Assert.Equal("high", view.Session.Thinking);
        Assert.True(view.Review.Independent);
        Assert.Equal(settings.Model, view.Review.Model?.Name);
        Assert.Equal("default", view.Review.Model?.Thinking);
    }
}
