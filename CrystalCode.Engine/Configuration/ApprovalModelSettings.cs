namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional reviewer model. While disabled, a stored provider and model are
/// kept and the session model reviews instead.
/// </summary>
public sealed record ApprovalModelSettings
{
    public static ApprovalModelSettings Off { get; } = new(false, null, null);

    public ApprovalModelSettings(bool enabled, string? provider, string? model)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (enabled && (Provider is null || Model is null))
        {
            throw new ArgumentException("Set an approval model before turning it on.");
        }

        Enabled = enabled;
    }

    public bool Enabled { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public bool HasSelection => Provider is not null && Model is not null;

    public ApprovalModelSettings EnabledCopy() => new(true, Provider, Model);

    public ApprovalModelSettings DisabledCopy() => new(false, Provider, Model);

    public string Describe()
    {
        var state = Enabled ? "On" : "Off";
        if (!HasSelection)
        {
            return "Approval model  " + state;
        }

        return "Approval model  " + state + "  " + Provider + "  " + Model;
    }
}
