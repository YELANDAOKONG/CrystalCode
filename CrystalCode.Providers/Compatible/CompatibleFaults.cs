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
        CompatibleWire.TryReadError(body, out var code, out var providerMessage);
        var message = code is null
            ? $"Chat request failed (HTTP {status})."
            : $"Chat request failed (HTTP {status}, code: {code}).";
        // Gateways often reply with a body that is not the OpenAI error shape.
        // Show a bounded copy of it so the operator still sees the reason.
        providerMessage = string.IsNullOrWhiteSpace(providerMessage)
            ? Truncate(body?.Trim())
            : providerMessage;
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

    public Exception FromResponseError(
        string vendorName,
        string? code,
        string? providerMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        return Create(
            BuildMessage(vendorName, "response", code, providerMessage),
            errorCode: code);
    }

    public Exception FromStreamError(
        string vendorName,
        string? code,
        string? providerMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        return Create(
            BuildMessage(vendorName, "chat stream", code, providerMessage),
            errorCode: code);
    }

    public Exception InvalidStreamBody(string vendorName, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);
        if (CompatibleWire.TryReadError(body, out var code, out var providerMessage))
        {
            return FromStreamError(vendorName, code, providerMessage);
        }

        return Create($"{vendorName} chat stream did not contain an event stream.");
    }

    private static string? Truncate(string? text)
    {
        const int maxLength = 500;
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }

    private static string BuildMessage(
        string vendorName,
        string phase,
        string? code,
        string? providerMessage)
    {
        var message = code is null
            ? $"{vendorName} {phase} failed."
            : $"{vendorName} {phase} failed (code: {code}).";
        return string.IsNullOrWhiteSpace(providerMessage)
            ? message
            : $"{message} {providerMessage}";
    }
}
