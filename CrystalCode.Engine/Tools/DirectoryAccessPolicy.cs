namespace CrystalCode.Engine.Tools;

/// <summary>
/// Session-local rules evaluated against canonical paths, independently of approval grants.
/// </summary>
internal sealed class DirectoryAccessPolicy
{
    private readonly List<DirectoryAccess> _rules = [];
    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public IReadOnlyList<DirectoryAccess> Snapshot() => Array.AsReadOnly(_rules.ToArray());

    public void Clear() => _rules.Clear();

    public void Set(string path, string mode)
    {
        if (mode is not ("read" or "write"))
        {
            throw new ArgumentException("Access mode must be read or write.", nameof(mode));
        }

        Remove(path);
        _rules.Add(new DirectoryAccess(path, mode));
    }

    public bool Remove(string path) =>
        _rules.RemoveAll(rule => string.Equals(rule.Path, path, Comparison)) > 0;

    public string? ModeFor(string fullPath, string workspaceRoot)
    {
        var mode = IsInside(fullPath, workspaceRoot) ? "write" : null;
        var matchedLength = mode is null ? -1 : workspaceRoot.Length;
        foreach (var rule in _rules)
        {
            if (rule.Path.Length >= matchedLength && IsInside(fullPath, rule.Path))
            {
                mode = rule.Mode;
                matchedLength = rule.Path.Length;
            }
        }

        return mode;
    }

    private static bool IsInside(string path, string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(Path.TrimEndingDirectorySeparator(path), prefix, Comparison)
            || path.StartsWith(Path.EndsInDirectorySeparator(prefix)
                ? prefix
                : prefix + Path.DirectorySeparatorChar, Comparison);
    }
}
