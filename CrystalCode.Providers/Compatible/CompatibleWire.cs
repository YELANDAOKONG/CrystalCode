using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Reasoning;

namespace CrystalCode.Providers.Compatible;

internal static class CompatibleWire
{
    public const string ChatCompletionsPath = "chat/completions";

    public const string UserAgent = "Crystal Code";

    public static OpaqueReasoningState CreateReasoningState(string format, string reasoningContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasoningContent);

        var bytes = Encoding.UTF8.GetBytes(reasoningContent);
        return new OpaqueReasoningState(format, bytes);
    }

    public static string ReadReasoningContent(
        CompatibleProfile profile,
        ReasoningContent content)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(content);

        if (content.State is not null)
        {
            if (profile.ReasoningStateFormat is null
                || !string.Equals(
                    content.State.Format,
                    profile.ReasoningStateFormat,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"{profile.VendorName} does not understand this opaque reasoning format.");
            }

            return Encoding.UTF8.GetString(content.State.Data.Span);
        }

        if (content.TextSegments.Count != 1)
        {
            throw new NotSupportedException(
                $"{profile.VendorName} accepts one readable reasoning segment per assistant turn.");
        }

        return content.TextSegments[0].Text;
    }

    public static FinishReason ReadFinishReason(string value)
    {
        return value switch
        {
            "stop" => FinishReason.Stop,
            "length" => FinishReason.Length,
            "content_filter" => FinishReason.ContentFilter,
            "tool_calls" => FinishReason.ToolCalls,
            _ => new FinishReason(value)
        };
    }

    public static TokenUsage? ReadUsage(JsonElement root, CompatibleFaults faults)
    {
        ArgumentNullException.ThrowIfNull(faults);

        if (!root.TryGetProperty("usage", out var usage)
            || usage.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (usage.ValueKind != JsonValueKind.Object)
        {
            throw faults.Create("Chat usage must be a JSON object.");
        }

        if (!TryReadInt64(usage, "prompt_tokens", out var promptTokens)
            || !TryReadInt64(usage, "completion_tokens", out var completionTokens))
        {
            throw faults.Create("Chat usage is missing prompt or completion token counts.");
        }

        long? reasoningTokens = null;
        if (usage.TryGetProperty("completion_tokens_details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && TryReadInt64(details, "reasoning_tokens", out var reportedReasoningTokens))
        {
            reasoningTokens = reportedReasoningTokens;
        }

        return new TokenUsage(promptTokens, completionTokens, reasoningTokens);
    }

    public static (string? Code, string? Message) ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? code = null;
            if (error.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String)
            {
                code = codeElement.GetString();
            }
            else if (error.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String)
            {
                code = typeElement.GetString();
            }

            string? message = null;
            if (error.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    public static bool TryReadInt64(JsonElement element, string name, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        value = property.GetInt64();
        return true;
    }

    public static bool TryReadString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    public static Uri NormalizeBaseUri(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        var text = baseUri.AbsoluteUri;
        if (text.EndsWith('/'))
        {
            return baseUri;
        }

        return new Uri(text + "/");
    }
}
