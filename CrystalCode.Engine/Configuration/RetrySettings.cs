using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Retry policy for one model-call family. Work turns and compaction read
/// <c>retry</c>; the approval reviewer reads <c>approvalRetry</c>.
/// </summary>
public sealed record RetrySettings
{
    public static RetrySettings Default { get; } = new();

    public RetrySettings(
        bool enabled = true,
        int maximumRetries = SessionRetryOptions.DefaultMaximumRetries,
        double initialDelaySeconds = SessionRetryOptions.DefaultInitialDelaySeconds,
        double backoffFactor = SessionRetryOptions.DefaultBackoffFactor,
        double jitterFactor = SessionRetryOptions.DefaultJitterFactor,
        double maximumDelaySeconds = SessionRetryOptions.DefaultMaximumDelaySeconds)
    {
        if (maximumRetries is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRetries),
                maximumRetries,
                "Maximum retries must be between 0 and 100.");
        }

        if (initialDelaySeconds is < 0 or > 3_600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialDelaySeconds),
                initialDelaySeconds,
                "Initial delay must be between 0 and 3600 seconds.");
        }

        if (backoffFactor is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backoffFactor),
                backoffFactor,
                "Backoff factor must be between 1 and 100.");
        }

        if (jitterFactor is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitterFactor),
                jitterFactor,
                "Jitter factor must be between 0 and 1.");
        }

        if (maximumDelaySeconds is < 0 or > 86_400)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDelaySeconds),
                maximumDelaySeconds,
                "Maximum delay must be between 0 and 86400 seconds.");
        }

        Enabled = enabled;
        MaximumRetries = maximumRetries;
        InitialDelaySeconds = initialDelaySeconds;
        BackoffFactor = backoffFactor;
        JitterFactor = jitterFactor;
        MaximumDelaySeconds = maximumDelaySeconds;
    }

    public bool Enabled { get; }

    public int MaximumRetries { get; }

    public double InitialDelaySeconds { get; }

    public double BackoffFactor { get; }

    public double JitterFactor { get; }

    public double MaximumDelaySeconds { get; }

    public SessionRetryOptions ToOptions() =>
        Enabled
            ? new SessionRetryOptions(
                MaximumRetries,
                initialDelay: TimeSpan.FromSeconds(InitialDelaySeconds),
                backoffFactor: BackoffFactor,
                jitterFactor: JitterFactor,
                maximumDelayWithoutHeaders: TimeSpan.FromSeconds(MaximumDelaySeconds))
            : SessionRetryOptions.None;
}
