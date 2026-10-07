using CrystalCode.Engine.Configuration;
using CrystalCode.Plugins.Models;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Maps the session's selected models onto the plugin-facing facts.
/// A disabled review selection is omitted here so a stored name never
/// leaks through the off switch.
/// </summary>
internal static class PluginModelFacts
{
    public static PluginModel Describe(
        ProviderDefinition provider,
        string modelName,
        ModelSettings model,
        string thinking)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(thinking);
        return new PluginModel(
            provider.Name.Value,
            provider.Protocol.Value,
            modelName.Trim(),
            model.ContextWindow,
            model.MaxTokens,
            model.Temperature,
            model.TopP,
            model.ImageInput,
            thinking.Trim());
    }

    public static PluginReview DescribeReview(
        ApprovalModelSettings approval,
        ProviderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!approval.Enabled || approval.Provider is null || approval.Model is null)
        {
            return PluginReview.UsingSession;
        }

        var providerName = new ProviderName(approval.Provider);
        var provider = catalog.GetModelProvider(providerName, approval.Model);
        var model = catalog.GetModel(providerName, approval.Model);
        return new PluginReview(
            true,
            Describe(provider, approval.Model, model, approval.ThinkingEffort.Value));
    }
}
