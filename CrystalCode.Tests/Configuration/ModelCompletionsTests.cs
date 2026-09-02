using CrystalCode.Configuration;
using CrystalCode.Display.Composer;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class ModelCompletionsTests
{
    [Fact]
    public void For_ListsCurrentProviderModelsThenProviders()
    {
        var catalog = ModelSelectionTests.Catalog();

        var options = ModelCompletions.For(catalog, ProviderName.DeepSeek);

        Assert.Contains(options, option => option.Name == "deepseek-v4-flash");
        Assert.Contains(options, option => option.Name == "deepseek-v4-pro");
        var openrouter = Assert.Single(options, option => option.Name == "openrouter");
        Assert.Equal("Provider", openrouter.Help);
        Assert.Contains(openrouter.ArgumentOptions, option => option.Name == "anthropic/claude-sonnet-4");
    }
}
