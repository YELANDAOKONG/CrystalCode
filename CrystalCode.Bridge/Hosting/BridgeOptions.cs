using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// What the executable hands to Bridge: process-only session overrides, the
/// fallback workspace for a session request without <c>cwd</c>, the plugin
/// registry, the version reported in <c>agentInfo</c>, where diagnostics go,
/// which session option ids a client sees, and an optional sink for the
/// protocol debug log. Stdout is never one of them;
/// it belongs to the protocol.
/// </summary>
/// <param name="ConfigOptions">The offered option ids, already expanded; null offers the built-in default.</param>
/// <param name="Debug">Where each protocol message is logged with secrets and long values removed; null logs nothing.</param>
public sealed record BridgeOptions(
    SessionLaunchOptions Launch,
    string WorkingDirectory,
    PluginRegistry Plugins,
    string Version,
    TextWriter Diagnostics,
    string? Workspace = null,
    bool Space = false,
    IReadOnlyList<string>? ConfigOptions = null,
    TextWriter? Debug = null)
{
    /// <summary>The ids of the session options a client sees as config options.</summary>
    internal IReadOnlySet<string> OfferedOptions =>
        new HashSet<string>(ConfigOptions ?? BridgeHostSettings.DefaultOptions, StringComparer.Ordinal);
}
