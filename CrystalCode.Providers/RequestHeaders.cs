namespace CrystalCode.Providers;

internal static class RequestHeaders
{
    private const string Host = "Host";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Accept", "Transfer-Encoding", "Connection"
    };

    public static IReadOnlyDictionary<string, string> Validate(
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyList<string> protocolHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is null || headers.Count == 0)
        {
            return result;
        }

        using var request = new HttpRequestMessage();
        foreach (var (name, value) in headers)
        {
            if (Reserved.Contains(name) || protocolHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Header '{name}' is reserved by the provider or HTTP transport.");
            }

            if (!result.TryAdd(name, value) || !CanSend(request, name, value))
            {
                throw new InvalidOperationException("Invalid or duplicate provider HTTP header.");
            }
        }

        return result;
    }

    public static void Apply(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (string.Equals(name, Host, StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Host = value;
                continue;
            }

            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static bool CanSend(HttpRequestMessage request, string name, string value)
    {
        if (value.Any(c => (c < ' ' && c != '\t') || c == '\u007f'))
        {
            return false;
        }

        try
        {
            if (string.Equals(name, Host, StringComparison.OrdinalIgnoreCase))
            {
                // The typed setter validates the authority before it can affect TLS.
                request.Headers.Host = value;
                return true;
            }

            return request.Headers.TryAddWithoutValidation(name, value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return false;
        }
    }
}
