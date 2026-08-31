using System.Net;
using System.Net.Http.Headers;

namespace CrystalCode.Providers.Compatible;

internal sealed class CompatibleFaults
{
    private readonly Func<string, int?, Exception?, string?, TimeSpan?, Exception> _create;

    public CompatibleFaults(
        Type exceptionType,
        Func<string, int?, Exception?, string?, TimeSpan?, Exception> create)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        ArgumentNullException.ThrowIfNull(create);
        if (!typeof(Exception).IsAssignableFrom(exceptionType))
        {
            throw new ArgumentException(
                "Fault type must be an exception.",
                nameof(exceptionType));
        }

        ExceptionType = exceptionType;
        _create = create;
    }

    public Type ExceptionType { get; }

    public Exception Create(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return _create(message, statusCode, innerException, errorCode, retryAfter);
    }

    public Exception FromResponse(
        HttpStatusCode statusCode,
        string body,
        HttpResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var status = (int)statusCode;
        var (code, providerMessage) = CompatibleWire.ReadError(body);
        var message = code is null
            ? $"Chat request failed (HTTP {status})."
            : $"Chat request failed (HTTP {status}, code: {code}).";
        if (!string.IsNullOrWhiteSpace(providerMessage))
        {
            message = $"{message} {providerMessage}";
        }

        return Create(
            message,
            status,
            errorCode: code,
            retryAfter: CompatibleRetryAfter.Read(headers));
    }
}
