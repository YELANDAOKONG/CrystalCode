namespace CrystalCode.Engine.Prompts;

/// <summary>
/// One prompt set or prompt attachment row for the command line.
/// </summary>
public sealed record PromptCatalogEntry(
    string DirectoryName,
    string Source,
    bool Enabled,
    bool Effective,
    string Title,
    string Description,
    int? Order,
    string ManifestPath,
    string Error)
{
    public override string ToString() => DirectoryName;
}
