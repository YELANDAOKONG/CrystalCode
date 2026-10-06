namespace CrystalCode.Engine.Sessions;

/// <summary>
/// What the operator is asked to trust. <see cref="TrustRoot"/> is the git
/// root when the workspace sits inside a repository, and the workspace
/// itself otherwise.
/// </summary>
public sealed record WorkspaceTrustRequest(string Workspace, string TrustRoot)
{
    public bool CoversRepository =>
        !string.Equals(
            Path.TrimEndingDirectorySeparator(Workspace),
            Path.TrimEndingDirectorySeparator(TrustRoot),
            StringComparison.Ordinal);
}
