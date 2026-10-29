namespace CrystalCode.Display.Shell;

/// <summary>
/// How a custom status-line field behaves when the line does not fit.
/// <paramref name="Keep"/> never hides it. <paramref name="HideBelow"/> hides it
/// whenever the terminal is narrower than that many columns.
/// </summary>
public sealed record StatusFieldRule(string Field, bool Keep = false, int? HideBelow = null);
