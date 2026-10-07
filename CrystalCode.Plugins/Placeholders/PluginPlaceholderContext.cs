using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Models;

namespace CrystalCode.Plugins.Placeholders;

/// <summary>
/// Facts available while a plugin placeholder is resolved. Strings the host
/// does not have yet are empty.
/// </summary>
public sealed record PluginPlaceholderContext
{
    public PluginPlaceholderContext(
        string mode,
        string workspaceRoot,
        string sessionId,
        string approval,
        string provider,
        string model,
        IPluginEnvironment environment,
        IPluginModels models)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(models);
        Mode = mode.Trim();
        WorkspaceRoot = workspaceRoot;
        SessionId = sessionId;
        Approval = approval;
        Provider = provider;
        Model = model;
        Environment = environment;
        Models = models;
    }

    public string Mode { get; }

    public string WorkspaceRoot { get; }

    public string SessionId { get; }

    public string Approval { get; }

    public string Provider { get; }

    public string Model { get; }

    public IPluginEnvironment Environment { get; }

    /// <summary>
    /// Live session and review model. Empty until the session publishes it.
    /// </summary>
    public IPluginModels Models { get; }

    public override string ToString() => nameof(PluginPlaceholderContext);
}
