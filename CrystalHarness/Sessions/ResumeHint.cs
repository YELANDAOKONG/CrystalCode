namespace CrystalHarness.Sessions;

/// <summary>
/// Exit copy printed after the alternate screen closes.
/// </summary>
public static class ResumeHint
{
    public static string ForSaved(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return
            "Session saved  " + sessionId.Trim() + Environment.NewLine
            + "Resume in this workspace with /resume" + Environment.NewLine
            + "Or /resume " + sessionId.Trim();
    }

    public static string ForWorkspace() =>
        "Resume the latest session in this workspace with /resume"
        + Environment.NewLine
        + "Resume a specific id with /resume <id>";
}
