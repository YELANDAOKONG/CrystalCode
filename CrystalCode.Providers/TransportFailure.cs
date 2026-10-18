namespace CrystalCode.Providers;

/// <summary>
/// Formats a transport failure for the operator. Session retry and the
/// terminal print <see cref="Exception.Message"/> and do not walk the
/// inner exception chain.
/// </summary>
internal static class TransportFailure
{
    public static string Describe(string summary, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(exception);

        var text = summary;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message.Trim();
            if (message.Length == 0 || text.Contains(message, StringComparison.Ordinal))
            {
                continue;
            }

            text = $"{text} {message}";
        }

        return text;
    }
}
