namespace CrystalCode.Plugins.Models;

/// <summary>
/// One captured pair of session and review models. The host normally hands
/// plugins a live <see cref="IPluginModels"/> instead of this snapshot.
/// </summary>
public sealed record PluginModels : IPluginModels
{
    public static PluginModels Empty { get; } = new(PluginModel.Empty, PluginReview.UsingSession);

    public PluginModels(PluginModel session, PluginReview review)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(review);
        Session = session;
        Review = review;
    }

    public PluginModel Session { get; }

    public PluginReview Review { get; }

    public override string ToString() => nameof(PluginModels);
}
