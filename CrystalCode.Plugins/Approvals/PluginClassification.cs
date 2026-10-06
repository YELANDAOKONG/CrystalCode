namespace CrystalCode.Plugins.Approvals;

/// <summary>Risk and authority a plugin assigns to an unknown tool.</summary>
public sealed record PluginClassification
{
    public PluginClassification(PluginRisk risk, PluginAuthority authority, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Risk = risk;
        Authority = authority;
        Summary = summary.Trim();
    }

    public PluginRisk Risk { get; }

    public PluginAuthority Authority { get; }

    public string Summary { get; }

    public override string ToString() => nameof(PluginClassification);
}
