using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Default directory rules for one canonical workspace root.
/// </summary>
internal sealed class WorkspaceAccessEntry
{
    public string Path { get; set; } = string.Empty;

    public List<DirectoryAccess> Directories { get; set; } = [];
}
