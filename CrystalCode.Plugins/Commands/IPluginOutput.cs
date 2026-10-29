namespace CrystalCode.Plugins.Commands;

/// <summary>Operator text a plugin slash command may write.</summary>
public interface IPluginOutput
{
    /// <summary>Writes one informational line.</summary>
    void Write(string text);

    /// <summary>Writes one error line.</summary>
    void Fail(string text);

    /// <summary>
    /// Writes a titled document the host draws, for example as a bordered card.
    /// A host that cannot draw one writes <see cref="PluginDocument.ToText"/>.
    /// </summary>
    void WriteDocument(PluginDocument document) => Write(document.ToText());
}
