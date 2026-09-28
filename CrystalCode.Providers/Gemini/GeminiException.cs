namespace CrystalCode.Providers.Gemini;

public sealed class GeminiException : ChatProviderException
{
    public GeminiException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null)
        : base(message, statusCode, innerException, errorCode, retryAfter)
    {
    }
}
