namespace CrystalHarness.Providers.DeepSeek;

/// <summary>
/// Raised when a DeepSeek provider operation fails.
/// </summary>
public sealed class DeepSeekException : Exception
{
    /// <summary>
    /// Initializes a DeepSeek provider exception.
    /// </summary>
    /// <param name="message">The English failure message.</param>
    /// <param name="statusCode">The optional HTTP status code.</param>
    /// <param name="innerException">The optional underlying failure.</param>
    public DeepSeekException(
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
