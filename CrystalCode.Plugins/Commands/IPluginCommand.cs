namespace CrystalCode.Plugins.Commands;

/// <summary>
/// An extra slash command. Built-in verbs and their aliases stay with the host.
/// </summary>
public interface IPluginCommand
{
    /// <summary>Gets the verb without a leading slash.</summary>
    string Name { get; }

    /// <summary>Gets the help line.</summary>
    string Help { get; }

    /// <summary>Runs the command.</summary>
    void Execute(string argument, IPluginOutput output);
}
