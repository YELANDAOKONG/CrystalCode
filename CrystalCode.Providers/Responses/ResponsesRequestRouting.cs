using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CrystalCode.Plugins.Clients;

namespace CrystalCode.Providers.Responses;

internal static class ResponsesRequestRouting
{
    private const int MaximumKeyLength = 64;
    private const string RequestIdHeader = "x-client-request-id";

    public static void WriteCacheKey(
        Utf8JsonWriter writer, ResponsesRequestShape shape, ProviderRequestContext? context)
    {
        if (shape.PromptCacheKey && context is not null)
        {
            writer.WriteString("prompt_cache_key", Affinity(context));
        }
    }

    public static void AddHeaders(
        HttpRequestMessage request, ResponsesRequestShape shape, ProviderRequestContext? context)
    {
        if (shape.SessionAffinity && context is not null)
        {
            request.Headers.Add("session-id", Affinity(context));
            request.Headers.Add("thread-id", Thread(context));
            var requestId = Guid.NewGuid().ToString("N");
            request.Headers.Add(RequestIdHeader, requestId);
            request.Options.Set(ProviderHttpRequestOptions.AdditionalAttempt, retry =>
            {
                // Leave an explicitly configured request ID alone.
                if (retry.Headers.TryGetValues(RequestIdHeader, out var values)
                    && values.SequenceEqual([requestId], StringComparer.Ordinal))
                {
                    retry.Headers.Remove(RequestIdHeader);
                    retry.Headers.Add(RequestIdHeader, Guid.NewGuid().ToString("N"));
                }
            });
        }
    }

    private static string Affinity(ProviderRequestContext context) =>
        !context.IsAuxiliary && context.ParentSessionId is { } parent ? WireId(parent) : Thread(context);

    private static string Thread(ProviderRequestContext context)
    {
        // Forks preserve the stored child ID and change the parent ID.
        if (context.ParentSessionId is { } parent)
        {
            var scope = context.IsAuxiliary ? "auxiliary" : "conversation";
            return HashId($"child:{scope}:{parent.Length}:{parent}:{context.SessionId}");
        }

        return WireId(context.IsAuxiliary ? context.SessionId + "-aux" : context.SessionId);
    }

    private static string WireId(string id) =>
        id.Length is > 0 and <= MaximumKeyLength
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? id
            : HashId(id);

    private static string HashId(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
}
