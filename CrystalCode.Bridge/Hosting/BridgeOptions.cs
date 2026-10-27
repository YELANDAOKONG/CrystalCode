using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// What the executable hands to Bridge: process-only session overrides, the
/// fallback workspace for a session request without <c>cwd</c>, the plugin
/// registry, the version reported in <c>agentInfo</c>, and where diagnostics
/// go. Stdout is never one of them; it belongs to the protocol.
/// </summary>
public sealed record BridgeOptions(
    SessionLaunchOptions Launch,
    string WorkingDirectory,
    PluginRegistry Plugins,
    string Version,
    TextWriter Diagnostics,
    string? Workspace = null,
    bool Space = false);
