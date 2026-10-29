namespace CrystalCode.Engine.Plugins.Interfaces;

/// <summary>
/// Session chrome used by a plugin slash command.
/// </summary>
public interface ISlashOutput
{
    void WriteNote(string text);

    void WriteError(string text);

    /// <summary>Writes a document the front end draws. The default is plain text.</summary>
    void WriteDocument(CrystalCode.Engine.Sessions.TranscriptDocument document) =>
        WriteNote(document.ToText());
}
