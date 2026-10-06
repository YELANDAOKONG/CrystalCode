namespace CrystalCode.Plugins.Approvals;

/// <summary>Where a plugin-classified tool call takes effect.</summary>
public enum PluginAuthority
{
    Workspace,
    OutsideWorkspace,
    Network,
    PrivilegedEscalation
}
