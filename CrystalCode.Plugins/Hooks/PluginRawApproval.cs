using CrystalCode.Plugins.Approvals;

namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// A full approval classification. A raw hook may set any risk, any
/// authority, any summary, and either prompt requirement.
/// </summary>
public sealed record PluginRawApproval
{
    public PluginRawApproval(
        PluginRisk risk,
        PluginAuthority authority,
        string summary,
        bool requirePrompt)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Risk = risk;
        Authority = authority;
        Summary = summary;
        RequirePrompt = requirePrompt;
    }

    public PluginRisk Risk { get; }

    public PluginAuthority Authority { get; }

    public string Summary { get; }

    public bool RequirePrompt { get; }

    public override string ToString() => nameof(PluginRawApproval);
}
