namespace CrystalCode.Sessions;

/// <summary>
/// Bounds and hooks for session-level model-round retries.
/// </summary>
public sealed class SessionRetryOptions
{
    public const int DefaultMaximumRetries = 5;

    public SessionRetryOptions(
        int maximumRetries = DefaultMaximumRetries,
        Func<TimeSpan, CancellationToken, Task>? waitAsync = null,
        Func<double>? jitter = null)
    {
        if (maximumRetries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRetries),
                maximumRetries,
                "Maximum retries cannot be negative.");
        }

        MaximumRetries = maximumRetries;
        WaitAsync = waitAsync ?? WaitAsyncCore;
        Jitter = jitter ?? DefaultJitter;
    }

    public static SessionRetryOptions Default { get; } = new();

    public static SessionRetryOptions None { get; } = new(0);

    public int MaximumRetries { get; }

    public Func<TimeSpan, CancellationToken, Task> WaitAsync { get; }

    public Func<double> Jitter { get; }

    private static Task WaitAsyncCore(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        return Task.Delay(delay, cancellationToken);
    }

    private static double DefaultJitter() => Random.Shared.NextDouble();
}
