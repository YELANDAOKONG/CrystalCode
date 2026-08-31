namespace CrystalCode.Tools.External;

internal static class ExternalPath
{
    public static bool IsInside(string root, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var prefix = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(fullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal)
            || string.Equals(
                Path.GetFullPath(fullPath),
                Path.GetFullPath(root),
                StringComparison.Ordinal);
    }
}
