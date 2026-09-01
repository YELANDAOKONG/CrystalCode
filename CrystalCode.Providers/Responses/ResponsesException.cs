namespace CrystalCode.Providers.Responses;

public sealed class ResponsesException : ChatProviderException
{
    public ResponsesException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null)
        : base(message, statusCode, innerException, errorCode, retryAfter)
    {
    }
}
