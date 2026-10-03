using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Lets the operator choose one saved session. A front end supplies the surface.
/// </summary>
public interface ISessionChooser
{
    /// <summary>
    /// Returns the chosen session id, or null when the operator declines.
    /// </summary>
    Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        CancellationToken cancellationToken);
}
