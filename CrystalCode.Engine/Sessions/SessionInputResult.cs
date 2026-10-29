namespace CrystalCode.Engine.Sessions;

/// <summary>
/// What one input submitted through <see cref="SessionRunner"/> did.
/// </summary>
/// <param name="TurnActive">A turn was running once the input was handled.</param>
/// <param name="Quit">The input asked the session to quit.</param>
/// <param name="Error">
/// The first error the engine wrote for this input, such as a refused
/// command, or null. Errors from a turn or side question it started are
/// not included.
/// </param>
/// <param name="Pending">The input recorded a setting that applies on the next turn.</param>
public sealed record SessionInputResult(bool TurnActive, bool Quit, string? Error = null, bool Pending = false)
{
    /// <summary>The engine handled the input without refusing it.</summary>
    public bool Accepted => Error is null;
}
