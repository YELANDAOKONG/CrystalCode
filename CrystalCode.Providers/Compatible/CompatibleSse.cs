using System.Runtime.CompilerServices;
using System.Text;

namespace CrystalCode.Providers.Compatible;

/// <summary>
/// Reads Chat Completions event data. A JSON document instead of an event
/// stream is reported as a provider fault rather than silently ending the
/// stream.
/// </summary>
internal static class CompatibleSse
{
    public static async IAsyncEnumerable<string> ReadDataAsync(
        StreamReader reader,
        CompatibleFaults faults,
        string vendorName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(faults);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // A JSON body can be mislabelled as an event stream by a gateway,
            // and a compact JSON error has no SSE framing. Inspect the line
            // rather than the Content-Type header.
            if (line.TrimStart().StartsWith('{'))
            {
                var payload = new StringBuilder(line);
                payload.Append('\n');
                payload.Append(
                    await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
                throw faults.InvalidStreamBody(vendorName, payload.ToString());
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data.Length == 0)
            {
                continue;
            }

            if (data == "[DONE]")
            {
                yield break;
            }

            yield return data;
        }
    }
}
