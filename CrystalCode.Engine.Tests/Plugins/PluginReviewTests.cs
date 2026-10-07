using CrystalCode.Plugins.Models;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginReviewTests
{
    private const int SampleContextWindow = 8;

    [Fact]
    public void Constructor_WhenIndependent_RequiresTheModel()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new PluginReview(true, null));

        Assert.Equal("model", error.ParamName);
    }

    [Fact]
    public void Constructor_WhenUsingTheSessionModel_RejectsAModel()
    {
        var model = SampleModel();

        var error = Assert.Throws<ArgumentException>(() => new PluginReview(false, model));

        Assert.Equal("model", error.ParamName);
        Assert.Null(PluginReview.UsingSession.Model);
        Assert.False(PluginReview.UsingSession.Independent);
    }

    private static PluginModel SampleModel() =>
        new(
            "openai",
            "openai",
            "gpt",
            SampleContextWindow,
            maxTokens: null,
            temperature: null,
            topP: null,
            imageInput: false,
            thinking: "low");
}
