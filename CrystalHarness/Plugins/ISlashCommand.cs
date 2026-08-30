namespace CrystalHarness.Plugins;

/// <summary>
/// An extra <c>/name</c> verb contributed by a plugin.
/// </summary>
public interface ISlashCommand
{
    string Name { get; }

    string Help { get; }

    void Execute(string argument, ISlashOutput output);
}
