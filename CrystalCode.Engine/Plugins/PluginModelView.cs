using CrystalCode.Plugins.Models;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Reads the session's current model on every access. Holding this object
/// across <c>/model</c> or <c>/approval model</c> still sees the new values.
/// </summary>
internal sealed class PluginModelView : IPluginModels
{
    private readonly Func<PluginModel> _session;
    private readonly Func<PluginReview> _review;

    public PluginModelView(Func<PluginModel> session, Func<PluginReview> review)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(review);
        _session = session;
        _review = review;
    }

    public PluginModel Session => _session();

    public PluginReview Review => _review();

    public override string ToString() => nameof(PluginModelView);
}
