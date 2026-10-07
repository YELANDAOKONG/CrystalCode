namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional reviewer model and its thinking gear. While disabled, a stored
/// provider, model, and gear are kept and the session model reviews instead.
/// </summary>
public sealed record ApprovalModelSettings
{
    public static ApprovalModelSettings Off { get; } = new(false, null, null);

    public ApprovalModelSettings(
        bool enabled,
        string? provider,
        string? model,
        ThinkingSelection? thinkingEffort = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (enabled && (Provider is null || Model is null))
        {
            throw new ArgumentException("Set an approval model before turning it on.");
        }

        Enabled = enabled;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
    }

    public bool Enabled { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public ThinkingSelection ThinkingEffort { get; }

    public bool HasSelection => Provider is not null && Model is not null;

    public ApprovalModelSettings EnabledCopy() => new(true, Provider, Model, ThinkingEffort);

    public ApprovalModelSettings DisabledCopy() => new(false, Provider, Model, ThinkingEffort);

    public ApprovalModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Enabled, Provider, Model, thinkingEffort);
    }

    public string Describe()
    {
        var state = Enabled ? "On" : "Off";
        if (!HasSelection)
        {
            return "Approval model  " + state;
        }

        var description = "Approval model  " + state + "  " + Provider + "  " + Model;
        if (ThinkingEffort != ThinkingSelection.Default)
        {
            description += "  ·  Think " + ThinkingLabel.For(ThinkingEffort);
        }

        return description;
    }
}
