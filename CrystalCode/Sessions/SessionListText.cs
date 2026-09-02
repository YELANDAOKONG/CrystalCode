using CrystalCode.Home;

namespace CrystalCode.Sessions;

/// <summary>
/// Formats session summaries for the transcript.
/// </summary>
internal static class SessionListText
{
    private const int PreviewLength = 60;

    public static string Format(
        IReadOnlyList<SessionSummary> sessions,
        string currentId,
        bool includeWorkspace)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentId);
        var lines = new List<string>(sessions.Count + 1)
        {
            includeWorkspace ? "All sessions" : "Sessions for this workspace"
        };
        foreach (var session in sessions)
        {
            var marker = string.Equals(session.Id, currentId, StringComparison.Ordinal)
                ? "*"
                : " ";
            var updated = session.UpdatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                ?? "Unknown time";
            var mode = session.PlanMode ? "Plan" : "Work";
            var workspace = includeWorkspace ? $"  {session.Workspace}" : string.Empty;
            lines.Add(
                $"{marker} {session.Id}  {updated}  {mode}  {session.UserTurns} turns"
                + workspace
                + $"  {TrimPreview(session.Preview)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string TrimPreview(string preview)
    {
        if (preview.Length <= PreviewLength)
        {
            return preview;
        }

        return preview[..(PreviewLength - 3)] + "...";
    }
}
