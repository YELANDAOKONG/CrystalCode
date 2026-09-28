namespace CrystalCode.Providers.Ollama;

public sealed class OllamaException : ChatProviderException
{
    public OllamaException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null)
        : base(message, statusCode, innerException, errorCode, retryAfter)
    {
    }
}
