namespace CrystalCode.Providers;

/// <summary>
/// Base type for DeepSeek and OpenAI-compatible adapter failures.
/// Session retry reads status, error code, and Retry-After from this type.
/// </summary>
public abstract class ChatProviderException : Exception
{
    protected ChatProviderException(
        string message,
        int? statusCode,
        Exception? innerException,
        string? errorCode,
        TimeSpan? retryAfter)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the HTTP status code when the failure came from an HTTP response.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Gets the provider error code when the response JSON included one.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the wait requested by Retry-After or retry-after-ms, when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
