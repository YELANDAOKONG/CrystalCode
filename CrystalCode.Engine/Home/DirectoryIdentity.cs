namespace CrystalCode.Engine.Home;

/// <summary>
/// Compares directory paths. A workspace at the home directory makes its
/// project <c>.crystal</c> directory the same directory as Home.
/// </summary>
internal static class DirectoryIdentity
{
    public static bool Same(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
