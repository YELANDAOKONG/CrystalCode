using System.Buffers;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;

namespace CrystalCode.Server.Endpoints;

/// <summary>Small helpers shared by the endpoint groups.</summary>
internal static class Json
{
    public const int MaximumBodyBytes = 1024 * 1024;

    public static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static IResult Ok(Action<Utf8JsonWriter> write) =>
        Results.Text(Write(write), "application/json", Encoding.UTF8);

    public static IResult Problem(int status, string detail) =>
        Results.Problem(detail: detail, statusCode: status);

    /// <summary>Reads a JSON object body. An empty body is an empty object.</summary>
    public static async Task<(JsonElement Body, string? Error)> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumBodyBytes)
        {
            return (default, "The request body is too large.");
        }

        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (stream.Length + read > MaximumBodyBytes)
            {
                return (default, "The request body is too large.");
            }

            stream.Write(buffer, 0, read);
        }

        if (stream.Length == 0)
        {
            using var empty = JsonDocument.Parse("{}");
            return (empty.RootElement.Clone(), null);
        }

        try
        {
            using var document = JsonDocument.Parse(stream.ToArray());
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? (document.RootElement.Clone(), null)
                : (default, "The request body must be a JSON object.");
        }
        catch (JsonException)
        {
            return (default, "The request body is not valid JSON.");
        }
    }

    public static string? String(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool? Boolean(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
