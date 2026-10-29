namespace CrystalCode.Plugins.Cli;

/// <summary>
/// A process command, meant to run as <c>crystal &lt;name&gt; ...</c> with no
/// session. The engine admits and lists it, but no executable entry point runs
/// it yet, so registering one has no effect today. The plugin would own its
/// arguments, its output, and its exit code.
/// </summary>
public interface IPluginCliCommand
{
    /// <summary>Gets the first argument that selects this command.</summary>
    string Name { get; }

    /// <summary>Gets the one-line description.</summary>
    string Help { get; }

    /// <summary>Runs the command and returns the process exit code.</summary>
    Task<int> RunAsync(PluginCliContext context, CancellationToken cancellationToken);
}
