namespace CrystalCode.Plugins.Commands;

/// <summary>Operator text a plugin slash command may write.</summary>
public interface IPluginOutput
{
    /// <summary>Writes one informational line.</summary>
    void Write(string text);

    /// <summary>Writes one error line.</summary>
    void Fail(string text);
}
