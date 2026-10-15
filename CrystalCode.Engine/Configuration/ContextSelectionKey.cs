namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Identifies one model for a saved local context budget. The provider name
/// is the normalized <see cref="ProviderName"/> value; the model id stays
/// opaque and may contain '/'.
/// </summary>
public sealed record ContextSelectionKey
{
    public ContextSelectionKey(ProviderName provider, string model)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        Provider = provider;
        Model = model.Trim();
    }

    public ProviderName Provider { get; }

    public string Model { get; }

    public override string ToString() => Provider.Value + "/" + Model;
}
