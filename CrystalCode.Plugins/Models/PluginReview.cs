namespace CrystalCode.Plugins.Models;

/// <summary>
/// Whether review uses its own model. A saved selection that is switched
/// off stays on the session model, and its provider and model are omitted.
/// </summary>
public sealed record PluginReview
{
    /// <summary>Review uses the session model.</summary>
    public static PluginReview UsingSession { get; } = new(false, null);

    public PluginReview(bool independent, PluginModel? model)
    {
        if (independent)
        {
            ArgumentNullException.ThrowIfNull(model);
        }
        else if (model is not null)
        {
            throw new ArgumentException(
                "Review model details are available only when review uses its own model.",
                nameof(model));
        }

        Independent = independent;
        Model = model;
    }

    /// <summary>
    /// True when review calls its own model. False when review calls the
    /// session model.
    /// </summary>
    public bool Independent { get; }

    /// <summary>
    /// The review model while <see cref="Independent"/> is true, and null
    /// while review uses the session model.
    /// </summary>
    public PluginModel? Model { get; }

    public override string ToString() => Independent ? "independent" : "session";
}
