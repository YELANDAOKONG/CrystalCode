namespace CrystalCode.Engine.Events;

/// <summary>
/// Receives every <see cref="SessionEvent"/> a session publishes.
/// </summary>
/// <remarks>
/// The session calls this from whichever thread produced the change, including
/// the turn thread while a model round streams. Implementations must be thread
/// safe and must return quickly.
/// </remarks>
public interface ISessionObserver
{
    void OnEvent(SessionEvent sessionEvent);
}
