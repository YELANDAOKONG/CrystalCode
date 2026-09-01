namespace CrystalCode.Configuration;

/// <summary>
/// Operator policy for author approval declarations by tool source.
/// </summary>
public sealed record ExternalToolApprovalSettings
{
    public static ExternalToolApprovalSettings Default { get; } = new(
        ExternalToolTrustPolicy.Author,
        ExternalToolTrustPolicy.Host);

    public ExternalToolApprovalSettings(
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

    public ExternalToolApprovalSettings WithHome(ExternalToolTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ExternalToolApprovalSettings(policy, Project);
    }

    public ExternalToolApprovalSettings WithProject(ExternalToolTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ExternalToolApprovalSettings(Home, policy);
    }
}
