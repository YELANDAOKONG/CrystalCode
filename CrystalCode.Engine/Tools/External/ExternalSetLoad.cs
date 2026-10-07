namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Whether one enabled tool set contributed any tool, and why it did not.
/// </summary>
public sealed record ExternalSetLoad(string DirectoryName, ExternalToolSource Source, bool Loaded, string Error)
{
    public override string ToString() => DirectoryName;
}
