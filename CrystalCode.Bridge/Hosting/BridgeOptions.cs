using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// What the executable hands to Bridge: process-only session overrides, the
/// fallback workspace for a session request without <c>cwd</c>, the plugin
/// registry, the version reported in <c>agentInfo</c>, where diagnostics go,
/// and which session option ids a client sees. Stdout is never one of them;
/// it belongs to the protocol.
/// </summary>
/// <param name="ConfigOptions">The offered option ids, already expanded; null offers the built-in default.</param>
public sealed record BridgeOptions(
    SessionLaunchOptions Launch,
    string WorkingDirectory,
    PluginRegistry Plugins,
    string Version,
    TextWriter Diagnostics,
    string? Workspace = null,
    bool Space = false,
    IReadOnlyList<string>? ConfigOptions = null)
{
    /// <summary>The ids of the session options a client sees as config options.</summary>
    internal IReadOnlySet<string> OfferedOptions =>
        new HashSet<string>(ConfigOptions ?? BridgeHostSettings.DefaultOptions, StringComparer.Ordinal);
}
