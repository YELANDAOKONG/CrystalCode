namespace CrystalCode.Engine.Plugins.Interfaces;

using CrystalCode.Plugins.Commands;

/// <summary>
/// An extra <c>/name</c> verb contributed by a plugin.
/// </summary>
public interface ISlashCommand
{
    string Name { get; }

    string Help { get; }

    IReadOnlyList<PluginCommandArgument> Arguments => [];

    void Execute(string argument, ISlashOutput output);
}
