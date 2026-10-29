namespace CrystalCode.Plugins.Commands;

/// <summary>
/// An extra slash command. A built-in verb or alias is refused unless
/// <see cref="OverridesBuiltIn"/> is true.
/// </summary>
public interface IPluginCommand
{
    /// <summary>Gets the verb without a leading slash.</summary>
    string Name { get; }

    /// <summary>Gets the help line.</summary>
    string Help { get; }

    /// <summary>
    /// Gets whether this command takes over a built-in verb or alias of the
    /// same name when the operator types it.
    /// </summary>
    bool OverridesBuiltIn => false;

    /// <summary>Runs the command.</summary>
    void Execute(string argument, IPluginOutput output);
}
