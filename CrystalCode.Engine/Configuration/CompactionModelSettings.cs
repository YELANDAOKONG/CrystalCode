namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Optional summarizer model and its thinking gear. While disabled, a stored
/// provider, model, and gear are kept and the session model summarizes instead.
/// </summary>
public sealed record CompactionModelSettings
{
    public static CompactionModelSettings Off { get; } = new(false, null, null);

    public CompactionModelSettings(
        bool enabled,
        string? provider,
        string? model,
        ThinkingSelection? thinkingEffort = null)
    {
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (enabled && (Provider is null || Model is null))
        {
            throw new ArgumentException("Set a compaction model before turning it on.");
        }

        Enabled = enabled;
        ThinkingEffort = thinkingEffort ?? ThinkingSelection.Default;
    }

    public bool Enabled { get; }

    public string? Provider { get; }

    public string? Model { get; }

    public ThinkingSelection ThinkingEffort { get; }

    public bool HasSelection => Provider is not null && Model is not null;

    public CompactionModelSettings EnabledCopy() => new(true, Provider, Model, ThinkingEffort);

    public CompactionModelSettings DisabledCopy() => new(false, Provider, Model, ThinkingEffort);

    public CompactionModelSettings WithThinkingEffort(ThinkingSelection thinkingEffort)
    {
        ArgumentNullException.ThrowIfNull(thinkingEffort);
        return new(Enabled, Provider, Model, thinkingEffort);
    }

    public string Describe()
    {
        var state = Enabled ? "On" : "Off";
        if (!HasSelection)
        {
            return "Compaction model  " + state;
        }

        var description = "Compaction model  " + state + "  " + Provider + "  " + Model;
        if (ThinkingEffort != ThinkingSelection.Default)
        {
            description += "  ·  Think " + ThinkingLabel.For(ThinkingEffort);
        }

        return description;
    }
}
