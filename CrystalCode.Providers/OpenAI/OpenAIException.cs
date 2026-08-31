namespace CrystalCode.Providers.OpenAI;

/// <summary>
/// Raised when an OpenAI provider operation fails.
/// </summary>
public sealed class OpenAIException : ChatProviderException
{
    /// <summary>
    /// Initializes an OpenAI provider exception.
    /// </summary>
    /// <param name="message">The English failure message.</param>
    /// <param name="statusCode">The optional HTTP status code.</param>
    /// <param name="innerException">The optional underlying failure.</param>
    /// <param name="errorCode">The optional provider error code.</param>
    /// <param name="retryAfter">The optional Retry-After wait.</param>
    public OpenAIException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null)
        : base(message, statusCode, innerException, errorCode, retryAfter)
    {
    }
}
