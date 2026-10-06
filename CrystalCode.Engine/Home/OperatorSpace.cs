using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

/// <summary>
/// The operator scratch workspace at <c>{home}/space</c>. Trust stops at
/// this directory. A parent git repository is not the trust root, so
/// opening the space cannot grant the rest of that repository.
/// </summary>
public static class OperatorSpace
{
    public static string Resolve(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return Workspace.Canonicalize(home.SpaceDirectory);
    }

    public static bool Is(CrystalHome home, string path)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Same(Resolve(home), Workspace.Canonicalize(Path.GetFullPath(path)));
    }

    public static string EnsureCreated(CrystalHome home)
    {
        var path = Resolve(home);
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool Same(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);
}
