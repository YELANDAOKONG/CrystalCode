namespace CrystalCode.Plugins.Hooks;

/// <summary>The host classification a plugin may only make stricter.</summary>
public sealed record PluginApprovalFacts
{
    public PluginApprovalFacts(string risk, string authority, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(risk);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentNullException.ThrowIfNull(summary);
        Risk = risk.Trim();
        Authority = authority.Trim();
        Summary = summary;
    }

    public string Risk { get; }

    public string Authority { get; }

    public string Summary { get; }

    public override string ToString() => nameof(PluginApprovalFacts);
}
