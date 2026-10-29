namespace CrystalCode.Engine.Configuration;

/// <summary>
/// How one custom status-line field behaves when the terminal is too narrow.
/// <paramref name="Keep"/> never hides the field. <paramref name="HideBelow"/>
/// hides it whenever the terminal is narrower than that many columns.
/// </summary>
public sealed record StatusLineRule(string Field, bool Keep = false, int? HideBelow = null);
