namespace CrystalCode.Sessions;

/// <summary>
/// A classified retryable model-round failure.
/// </summary>
public sealed record SessionRetryable(string Message, TimeSpan? RetryAfter);
