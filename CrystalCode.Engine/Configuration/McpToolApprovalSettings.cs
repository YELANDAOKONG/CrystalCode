using CrystalCode.Engine.Mcp;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Operator policy for trusting an MCP server's read-only tool declarations,
/// by where the server definition came from. Host reviews every call.
/// </summary>
public sealed record McpToolApprovalSettings
{
    public static McpToolApprovalSettings Default { get; } = new(
        ExternalToolTrustPolicy.Host,
        ExternalToolTrustPolicy.Host);

    public McpToolApprovalSettings(
        ExternalToolTrustPolicy home,
        ExternalToolTrustPolicy project)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(project);
        Home = home;
        Project = project;
    }

    public ExternalToolTrustPolicy Home { get; }

    public ExternalToolTrustPolicy Project { get; }

    public McpToolApprovalSettings WithHome(ExternalToolTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new McpToolApprovalSettings(policy, Project);
    }

    public McpToolApprovalSettings WithProject(ExternalToolTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new McpToolApprovalSettings(Home, policy);
    }

    /// <summary>
    /// Session servers come from the front end, not from the operator's Home,
    /// so they follow the Project policy.
    /// </summary>
    public ExternalToolTrustPolicy For(McpServerSource source) =>
        source == McpServerSource.Home ? Home : Project;
}
