using CrystalCode.Plugins.Approvals;

namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// A request to raise risk or force another operator prompt.
/// Advice that would lower risk is ignored.
/// </summary>
public sealed record PluginApprovalAdvice(PluginRisk? Risk = null, bool RequirePrompt = false)
{
    public override string ToString() => nameof(PluginApprovalAdvice);
}
