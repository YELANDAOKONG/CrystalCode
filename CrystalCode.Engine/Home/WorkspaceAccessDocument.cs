namespace CrystalCode.Engine.Home;

/// <summary>
/// JSON shape for <c>~/.crystal/access.json</c>.
/// </summary>
internal sealed class WorkspaceAccessDocument
{
    public List<WorkspaceAccessEntry> Workspaces { get; set; } = [];
}
