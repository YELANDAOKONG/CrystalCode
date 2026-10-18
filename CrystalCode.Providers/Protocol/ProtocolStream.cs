using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.Protocol;

/// <summary>
/// Reads protocol event data. A JSON document instead of an event stream, or
/// pretty-printed JSON instead of JSON lines, is a provider fault rather than
/// a stream that ended early.
/// </summary>
internal static class ProtocolStream
{
    public static async IAsyncEnumerable<string> ReadDataAsync(
        StreamReader reader,
        bool jsonLines,
        string vendorName,
        Func<string, string?, Exception?, Exception> create,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // A JSON body can be mislabelled as an event stream by a gateway,
            // and a compact JSON error has no SSE framing. Inspect the line
            // rather than the Content-Type header.
            if (!jsonLines && line.TrimStart().StartsWith('{'))
            {
                var body = await ReadRemainderAsync(reader, line, cancellationToken)
                    .ConfigureAwait(false);
                throw ProtocolFaults.FromBody(body, vendorName, "stream", create);
            }

            if (!jsonLines && !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = jsonLines ? line : line[5..].TrimStart();
            if (data.Length == 0 || data == "[DONE]")
            {
                continue;
            }

            if (jsonLines && !IsJsonLine(data))
            {
                var body = await ReadRemainderAsync(reader, data, cancellationToken)
                    .ConfigureAwait(false);
                throw JsonLineBody(body, vendorName, create);
            }

            yield return data;
        }
    }

    private static bool IsJsonLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 1 && trimmed[0] == '{' && trimmed[^1] == '}';
    }

    private static Exception JsonLineBody(
        string body,
        string vendorName,
        Func<string, string?, Exception?, Exception> create)
    {
        if (CompatibleWire.TryReadError(body, out var code, out var providerMessage))
        {
            return create(
                ProtocolFaults.Describe(vendorName, "stream", code, providerMessage),
                code,
                null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            return create($"{vendorName} stream contained invalid JSON.", null, exception);
        }

        return create($"{vendorName} stream did not contain JSON lines.", null, null);
    }

    private static async Task<string> ReadRemainderAsync(
        StreamReader reader,
        string firstLine,
        CancellationToken cancellationToken)
    {
        var payload = new StringBuilder(firstLine);
        payload.Append('\n');
        payload.Append(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        return payload.ToString();
    }
}
