using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

using CrystalCode.Providers;

namespace CrystalCode.Sessions;

/// <summary>
/// Classifies model-round failures and computes backoff, matching OpenCode session retry.
/// </summary>
public static class SessionRetry
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);

    public const double BackoffFactor = 2;

    public const double JitterFactor = 0.25;

    public static readonly TimeSpan MaximumDelayWithoutHeaders = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    private static readonly HashSet<string> NonRetryableCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "insufficient_quota",
        "usage_not_included",
        "invalid_prompt",
        "context_length_exceeded"
    };

    private static readonly Regex[] RetryablePatterns =
    [
        new("429|500|502|503|504|524", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "rate increased too quickly|rate limit|rate-limit|rate_limit|too many requests",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "overloaded|service unavailable|service_unavailable|service-unavailable|internal error|internal_error|internal server error|server error|server_error|server-error|provider returned error|provider_returned_error|provider-returned-error",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "terminated|fetch failed|failed to fetch|network[-_\\s]error|upstream connect|connection error|connection refused|connection lost|socket connection was closed|socket hang up|reset before headers|getaddrinfo|enotfound|eai_again|econnrefused|econnreset|etimedout",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "^timeout$|\\b(?:request|response|connection|network|stream|read) (?:timeout|timed out|time out)\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "try your request again|retry your request|resource exhausted|resource_exhausted",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "\\btry again (?:later|in\\b)|\\b(?:currently|temporarily) at capacity\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        new(
            "stream ended|ended before (?:every )?candidate",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)
    ];

    public static TimeSpan Delay(int attempt, TimeSpan? retryAfter, double jitter)
    {
        if (attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be positive.");
        }

        if (jitter is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitter), jitter, "Jitter must be between 0 and 1.");
        }

        if (retryAfter is { } header)
        {
            return Cap(header, MaximumDelay);
        }

        var baseMilliseconds = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 1);
        var withJitter = baseMilliseconds + (baseMilliseconds * JitterFactor * jitter);
        var exponential = TimeSpan.FromMilliseconds(Math.Ceiling(withJitter));
        return Cap(Min(exponential, MaximumDelayWithoutHeaders), MaximumDelay);
    }

    public static bool TryDescribe(Exception exception, out SessionRetryable retryable)
    {
        ArgumentNullException.ThrowIfNull(exception);
        retryable = null!;
        var fault = FindFault(exception);
        var text = CombinedText(exception);
        if (IsOverflow(fault, text) || IsNonRetryableCode(fault) || IsNonRetryableStatus(fault))
        {
            return false;
        }

        if (IsRetryableStatus(fault)
            || MatchesRetryableMessage(text)
            || IsTransientTransport(exception))
        {
            retryable = new SessionRetryable(OperatorMessage(exception, text), fault?.RetryAfter);
            return true;
        }

        return false;
    }

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> action,
        SessionRetryOptions options,
        Action<SessionRetryAttempt>? onRetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var attempt = 0;
        while (true)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                attempt++;
                if (attempt > options.MaximumRetries || !TryDescribe(exception, out var retryable))
                {
                    throw;
                }

                var delay = Delay(attempt, retryable.RetryAfter, options.Jitter());
                onRetry?.Invoke(new SessionRetryAttempt(attempt, retryable.Message, delay));
                await options.WaitAsync(delay, cancellationToken);
            }
        }
    }

    private static ChatProviderException? FindFault(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ChatProviderException fault)
            {
                return fault;
            }
        }

        return null;
    }

    private static bool IsOverflow(ChatProviderException? fault, string text)
    {
        if (fault?.ErrorCode is not null
            && fault.ErrorCode.Equals("context_length_exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
            || text.Contains("context window", StringComparison.OrdinalIgnoreCase)
            || text.Contains("maximum context", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNonRetryableCode(ChatProviderException? fault)
    {
        return fault?.ErrorCode is not null && NonRetryableCodes.Contains(fault.ErrorCode);
    }

    private static bool IsNonRetryableStatus(ChatProviderException? fault)
    {
        return fault?.StatusCode is 401 or 403;
    }

    private static bool IsRetryableStatus(ChatProviderException? fault)
    {
        if (fault?.StatusCode is not { } status)
        {
            return false;
        }

        return status is 404 or 408 or 429 || status >= 500;
    }

    private static bool MatchesRetryableMessage(string text)
    {
        foreach (var pattern in RetryablePatterns)
        {
            if (pattern.IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientTransport(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or IOException or SocketException or TimeoutException)
            {
                return true;
            }

            if (current is TaskCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static string CombinedText(Exception exception)
    {
        var builder = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(current.Message);
            if (current is ChatProviderException { ErrorCode: { Length: > 0 } code })
            {
                builder.Append('\n');
                builder.Append(code);
            }
        }

        return builder.ToString();
    }

    private static string OperatorMessage(Exception exception, string text)
    {
        if (text.Contains("too_many_requests", StringComparison.OrdinalIgnoreCase))
        {
            return "Too Many Requests";
        }

        if (text.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
            || text.Contains("resource_exhausted", StringComparison.OrdinalIgnoreCase)
            || text.Contains("resource exhausted", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return "Provider is overloaded";
        }

        return exception.Message;
    }

    private static TimeSpan Cap(TimeSpan value, TimeSpan maximum) =>
        value > maximum ? maximum : value;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left < right ? left : right;
}
