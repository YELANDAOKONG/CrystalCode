namespace CrystalCode.Plugins.Cli;

/// <summary>Facts and streams for one <see cref="IPluginCliCommand"/> run.</summary>
public sealed record PluginCliContext
{
    public PluginCliContext(
        string workspaceRoot,
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        WorkspaceRoot = workspaceRoot;
        Arguments = [.. arguments];
        Output = output;
        Error = error;
    }

    /// <summary>Gets the current directory of the process.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>Gets the arguments after the command name, unparsed.</summary>
    public IReadOnlyList<string> Arguments { get; }

    public TextWriter Output { get; }

    public TextWriter Error { get; }

    public override string ToString() => nameof(PluginCliContext);
}
