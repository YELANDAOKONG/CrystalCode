namespace CrystalCode.Providers.OpenAI;

/// <summary>
/// Raised when an OpenAI provider operation fails.
/// </summary>
public sealed class OpenAIException : Exception
{
    /// <summary>
    /// Initializes an OpenAI provider exception.
    /// </summary>
    /// <param name="message">The English failure message.</param>
    /// <param name="statusCode">The optional HTTP status code.</param>
    /// <param name="innerException">The optional underlying failure.</param>
    public OpenAIException(
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the HTTP status code when the failure came from an HTTP response.
    /// </summary>
    public int? StatusCode { get; }
}
