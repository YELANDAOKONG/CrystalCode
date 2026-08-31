namespace CrystalCode.Display.Paint;

/// <summary>
/// Shortens workspace paths the way coding CLIs do: home becomes ~.
/// </summary>
public static class PathDisplay
{
    public static string Shorten(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)
            && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..].Replace('\\', '/');
        }

        return path.Replace('\\', '/');
    }
}
