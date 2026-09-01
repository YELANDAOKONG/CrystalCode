namespace CrystalCode.Providers.Anthropic;

public sealed class AnthropicException : ChatProviderException
{
    public AnthropicException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null)
        : base(message, statusCode, innerException, errorCode, retryAfter)
    {
    }
}
