using System.Text.Json;

using Microsoft.AspNetCore.Http;

namespace CrystalCode.Server.Security;

/// <summary>Writes <c>application/problem+json</c> responses with English text.</summary>
public static class Problems
{
    public static async Task WriteAsync(HttpContext context, int status, string detail)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("type", "about:blank");
        writer.WriteString("title", ReasonPhrases(status));
        writer.WriteNumber("status", status);
        writer.WriteString("detail", detail);
        writer.WriteEndObject();
        await writer.FlushAsync();
    }

    private static string ReasonPhrases(int status) =>
        Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase
            ? phrase
            : "Error";
}
