namespace CrystalCode.Engine.Sessions;

/// <summary>
/// What one input submitted through <see cref="SessionRunner"/> did.
/// </summary>
/// <param name="TurnActive">A turn was running once the input was handled.</param>
/// <param name="Quit">The input asked the session to quit.</param>
public sealed record SessionInputResult(bool TurnActive, bool Quit);
