namespace CrystalHarness.Plugins.Interfaces;

/// <summary>
/// Session chrome used by a plugin slash command.
/// </summary>
public interface ISlashOutput
{
    void WriteNote(string text);

    void WriteError(string text);
}
