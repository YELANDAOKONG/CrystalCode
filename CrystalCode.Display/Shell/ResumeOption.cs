namespace CrystalCode.Display.Shell;

/// <summary>Saved-session metadata for the terminal resume page.</summary>
public sealed record ResumeOption(
    string Id,
    string Title,
    string Preview,
    string Workspace,
    DateTimeOffset? UpdatedUtc,
    int UserTurns);
