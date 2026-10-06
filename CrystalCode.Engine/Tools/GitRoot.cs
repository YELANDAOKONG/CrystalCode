namespace CrystalCode.Engine.Tools;

/// <summary>
/// Finds the git repository that contains a directory. A root has a
/// <c>.git</c> directory or a <c>.git</c> file, the same rule instruction
/// and skill discovery use.
/// </summary>
public static class GitRoot
{
    public static string? Find(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var current = Path.GetFullPath(start);
        while (true)
        {
            var git = Path.Combine(current, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                return null;
            }

            current = parent.FullName;
        }
    }

    /// <summary>
    /// Directory a trust decision applies to. Inside a git repository that
    /// is the repository root. Otherwise it is the workspace itself.
    /// </summary>
    public static string TrustRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var canonical = Workspace.Canonicalize(Path.GetFullPath(workspaceRoot));
        return Find(canonical) ?? canonical;
    }
}
