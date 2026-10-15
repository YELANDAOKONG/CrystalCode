namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Bounds, timing, and hooks for session-level model-round retries.
/// </summary>
public sealed class SessionRetryOptions
{
    public const int DefaultMaximumRetries = 5;

    public const double DefaultInitialDelaySeconds = 2;

    public const double DefaultBackoffFactor = 2;

    public const double DefaultJitterFactor = 0.25;

    public const double DefaultMaximumDelaySeconds = 30;

    public SessionRetryOptions(
        int maximumRetries = DefaultMaximumRetries,
        Func<TimeSpan, CancellationToken, Task>? waitAsync = null,
        Func<double>? jitter = null,
        TimeSpan? initialDelay = null,
        double backoffFactor = DefaultBackoffFactor,
        double jitterFactor = DefaultJitterFactor,
        TimeSpan? maximumDelayWithoutHeaders = null)
    {
        if (maximumRetries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRetries),
                maximumRetries,
                "Maximum retries cannot be negative.");
        }

        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(DefaultInitialDelaySeconds);
        if (InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialDelay),
                InitialDelay,
                "Initial delay cannot be negative.");
        }

        if (!double.IsFinite(backoffFactor) || backoffFactor < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backoffFactor),
                backoffFactor,
                "Backoff factor must be at least 1.");
        }

        if (!double.IsFinite(jitterFactor) || jitterFactor is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitterFactor),
                jitterFactor,
                "Jitter factor must be between 0 and 1.");
        }

        MaximumDelayWithoutHeaders =
            maximumDelayWithoutHeaders ?? TimeSpan.FromSeconds(DefaultMaximumDelaySeconds);
        if (MaximumDelayWithoutHeaders < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDelayWithoutHeaders),
                MaximumDelayWithoutHeaders,
                "Maximum delay cannot be negative.");
        }

        MaximumRetries = maximumRetries;
        BackoffFactor = backoffFactor;
        JitterFactor = jitterFactor;
        WaitAsync = waitAsync ?? WaitAsyncCore;
        Jitter = jitter ?? DefaultJitter;
    }

    public static SessionRetryOptions Default { get; } = new();

    public static SessionRetryOptions None { get; } = new(0);

    public int MaximumRetries { get; }

    public TimeSpan InitialDelay { get; }

    public double BackoffFactor { get; }

    public double JitterFactor { get; }

    public TimeSpan MaximumDelayWithoutHeaders { get; }

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
