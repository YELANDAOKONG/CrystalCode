using System.Globalization;
using System.Text.Json;

namespace CrystalCode.Bridge.Transport;

/// <summary>
/// Writes one line per JSON-RPC message in either direction to a debug
/// writer, so an operator can see what an editor and Bridge said to each
/// other. Values that can carry a secret or a large payload never reach the
/// log: editor MCP server definitions and credential-like fields are replaced,
/// and long strings (image data, file text, tool output) are cut.
/// </summary>
internal sealed class ProtocolLog
{
    internal const int MaxStringLength = 256;
    internal const string Redacted = "[redacted]";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mcpServers",
        "env",
        "headers",
        "apiKey",
        "api_key",
        "token",
        "accessToken",
        "authorization",
        "password",
        "secret",
        "credentials"
    };

    private readonly TextWriter _writer;
    private readonly object _gate = new();
    private bool _failed;

    public ProtocolLog(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <summary>Logs a line read from the client.</summary>
    public void Incoming(string line) => Write("client -> bridge", line);

    /// <summary>Logs a message Bridge is sending to the client.</summary>
    public void Outgoing(ReadOnlySpan<byte> message) =>
        Write("bridge -> client", System.Text.Encoding.UTF8.GetString(message));

    /// <summary>Returns the message with sensitive and long values replaced.</summary>
    internal static string Sanitize(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return "<not valid JSON, " + json.Length.ToString(CultureInfo.InvariantCulture) + " characters not logged>";
        }

        using (document)
        {
            var bytes = JsonText.Write(writer => WriteValue(writer, document.RootElement));
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    private void Write(string direction, string json) => Emit(direction, Sanitize(json));

    private void Emit(string direction, string text)
    {
        lock (_gate)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                _writer.WriteLine(
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                    + " [" + direction + "] " + text);
                _writer.Flush();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // A broken debug sink must not take the session down.
                _failed = true;
            }
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    if (SensitiveNames.Contains(property.Name))
                    {
                        writer.WriteString(property.Name, Redacted);
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(Shorten(value.GetString() ?? string.Empty));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string Shorten(string text)
    {
        if (text.Length <= MaxStringLength)
        {
            return text;
        }

        var keep = MaxStringLength;
        if (char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "... (" + text.Length.ToString(CultureInfo.InvariantCulture) + " characters)";
    }
}
