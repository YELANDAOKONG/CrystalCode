using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Modes;

/// <summary>
/// Checks a mode name a front end received before a session exists.
/// </summary>
public static class ModeSelection
{
    /// <summary>
    /// True for <c>plan</c>, <c>work</c>, or an enabled custom mode visible
    /// from <paramref name="workspaceRoot"/>.
    /// </summary>
    public static bool TryResolve(
        CrystalHome home,
        string workspaceRoot,
        string name,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        error = string.Empty;
        var requested = name?.Trim() ?? string.Empty;
        if (requested.Equals(ModeCatalog.Plan, StringComparison.OrdinalIgnoreCase)
            || requested.Equals(ModeCatalog.Work, StringComparison.OrdinalIgnoreCase)
            || requested.Length > 0 && ModeCatalog.Load(home, workspaceRoot).Find(requested) is not null)
        {
            return true;
        }

        error = "Mode not found or not enabled  " + requested;
        return false;
    }
}
