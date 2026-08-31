using CrystalCode.Configuration;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class ModelSelectionTests
{
    [Fact]
    public void TryResolve_SelectsModelOnCurrentProvider()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "deepseek-v4-pro",
            out var selection,
            out var error);

        Assert.True(resolved);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ProviderName.DeepSeek, selection!.Provider);
        Assert.Equal("deepseek-v4-pro", selection.Model);
    }

    [Fact]
    public void TryResolve_PrefersCurrentProviderWhenNameAlsoExistsElsewhere()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            new ProviderName("alpha"),
            "shared-model",
            out var selection,
            out _);

        Assert.True(resolved);
        Assert.Equal("alpha", selection!.Provider.Value);
    }

    [Fact]
    public void TryResolve_UsesUniqueCatalogModel()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "anthropic/claude-sonnet-4",
            out var selection,
            out _);

        Assert.True(resolved);
        Assert.Equal("openrouter", selection!.Provider.Value);
        Assert.Equal("anthropic/claude-sonnet-4", selection.Model);
    }

    [Fact]
    public void TryResolve_RejectsAmbiguousModelName()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "shared-model",
            out var selection,
            out var error);

        Assert.False(resolved);
        Assert.Null(selection);
        Assert.Contains("more than one provider", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_SelectsSingleModelProviderByName()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "groq",
            out var selection,
            out _);

        Assert.True(resolved);
        Assert.Equal("groq", selection!.Provider.Value);
        Assert.Equal("llama", selection.Model);
    }

    [Fact]
    public void TryResolve_RequiresModelWhenProviderHasSeveral()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "openai",
            out var selection,
            out var error);

        Assert.False(resolved);
        Assert.Null(selection);
        Assert.Contains("/model openai <model>", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_SwitchesProviderAndSlashedModelId()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "openrouter anthropic/claude-sonnet-4",
            out var selection,
            out _);

        Assert.True(resolved);
        Assert.Equal("openrouter", selection!.Provider.Value);
        Assert.Equal("anthropic/claude-sonnet-4", selection.Model);
    }

    [Fact]
    public void TryResolve_TwoTokensOnCurrentProvider()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "deepseek deepseek-v4-pro",
            out var selection,
            out _);

        Assert.True(resolved);
        Assert.Equal(ProviderName.DeepSeek, selection!.Provider);
        Assert.Equal("deepseek-v4-pro", selection.Model);
    }

    [Fact]
    public void TryResolve_UnknownProvider()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "missing gpt",
            out var selection,
            out var error);

        Assert.False(resolved);
        Assert.Null(selection);
        Assert.Contains("Provider 'missing' is not configured", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_UnknownModelOnNamedProvider()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "openai missing",
            out var selection,
            out var error);

        Assert.False(resolved);
        Assert.Null(selection);
        Assert.Contains("not configured for provider 'openai'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_UnknownOneToken()
    {
        var catalog = Catalog();

        var resolved = ModelSelection.TryResolve(
            catalog,
            ProviderName.DeepSeek,
            "no-such-model",
            out var selection,
            out var error);

        Assert.False(resolved);
        Assert.Null(selection);
        Assert.Contains("not configured", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatCatalog_MarksCurrentModel()
    {
        var catalog = Catalog();

        var text = ModelSelection.FormatCatalog(
            catalog,
            ProviderName.DeepSeek,
            "deepseek-v4-flash");

        Assert.Contains("deepseek-v4-flash  (current)", text, StringComparison.Ordinal);
        Assert.Contains("openrouter", text, StringComparison.Ordinal);
        Assert.Contains("anthropic/claude-sonnet-4", text, StringComparison.Ordinal);
        Assert.DoesNotContain("gpt-5.6-sol  (current)", text, StringComparison.Ordinal);
    }

    internal static ProviderCatalog Catalog()
    {
        return ProviderCatalog.CreateStarter().Overlay(
        [
            new ProviderDefinition(
                new ProviderName("openrouter"),
                ProviderProtocol.OpenAI,
                new Uri("https://openrouter.ai/api/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["anthropic/claude-sonnet-4"] = new(200000)
                }),
            new ProviderDefinition(
                new ProviderName("groq"),
                ProviderProtocol.OpenAI,
                new Uri("https://api.groq.com/openai/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["llama"] = new(131072)
                }),
            new ProviderDefinition(
                new ProviderName("alpha"),
                ProviderProtocol.OpenAI,
                new Uri("https://alpha.example/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["shared-model"] = new(1000)
                }),
            new ProviderDefinition(
                new ProviderName("beta"),
                ProviderProtocol.OpenAI,
                new Uri("https://beta.example/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["shared-model"] = new(1000)
                })
        ]);
    }
}
