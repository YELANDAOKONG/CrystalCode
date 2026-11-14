using CrystalCode.Display.Shell;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions.Interfaces;

namespace CrystalCode.Terminal;

/// <summary>Opens the same saved-session page inside a running session and at startup.</summary>
internal sealed class SessionPicker(SessionRenderer renderer) : ISessionChooser
{
    public Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var options = sessions.Select(static session => new ResumeOption(
            session.Id,
            session.Title,
            session.Preview,
            session.Workspace,
            session.UpdatedUtc,
            session.UserTurns)).ToArray();
        return renderer.ReadResumePickerAsync(new ResumePicker(options, currentId, listWorkspace), cancellationToken);
    }
}
