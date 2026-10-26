using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Version;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// What the process gives the server besides its options: where to write
/// the listening line and warnings, the plugin registry, the build identity
/// reported by <c>/info</c>, and whether a generated token may be shown
/// (only when the error stream is an operator's terminal).
/// </summary>
public sealed record ServerRuntime(
    TextWriter Output,
    TextWriter Error,
    PluginRegistry Plugins,
    BuildIdentity Build,
    bool ShowGeneratedToken = false);
