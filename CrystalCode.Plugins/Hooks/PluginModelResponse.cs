using Crystal;
using Crystal.Chat;

namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// One completed model call. The items are the returned candidate. The host
/// does not apply changes made to this value.
/// </summary>
public sealed record PluginModelResponse
{
    public PluginModelResponse(
        PluginModelPurpose purpose,
        FinishReason finishReason,
        IEnumerable<PluginModelItem> items,
        TokenUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(finishReason);
        ArgumentNullException.ThrowIfNull(items);
        Purpose = purpose;
        FinishReason = finishReason;
        Items = [.. items];
        Usage = usage;
    }

    public PluginModelPurpose Purpose { get; }

    public FinishReason FinishReason { get; }

    public IReadOnlyList<PluginModelItem> Items { get; }

    public TokenUsage? Usage { get; }

    public override string ToString() => nameof(PluginModelResponse);
}
