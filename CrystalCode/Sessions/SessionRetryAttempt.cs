namespace CrystalCode.Sessions;

/// <summary>
/// One scheduled wait before repeating the same model round.
/// </summary>
public sealed record SessionRetryAttempt(int Attempt, string Message, TimeSpan Delay);
