using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Reasoning;

using CrystalCode.Plugins.Clients;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Compatible;

internal static class CompatibleWire
{
    public const string ChatCompletionsPath = "chat/completions";

    public const string OrganizationHeader = "OpenAI-Organization";
    public const string ProjectHeader = "OpenAI-Project";

    public const string UserAgent = "Crystal Code";

    public static OpaqueReasoningState CreateReasoningState(string format, string reasoningContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasoningContent);

        var bytes = Encoding.UTF8.GetBytes(reasoningContent);
        return new OpaqueReasoningState(format, bytes);
    }

    public static bool TryReadReasoningContent(
        CompatibleProfile profile,
        ReasoningContent content,
        out string reasoningContent)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(content);

        reasoningContent = string.Empty;
        if (content.State is not null)
        {
            if (profile.ReasoningStateFormat is null
                || !string.Equals(
                    content.State.Format,
                    profile.ReasoningStateFormat,
                    StringComparison.Ordinal))
            {
                return false;
            }

            reasoningContent = Encoding.UTF8.GetString(content.State.Data.Span);
            return true;
        }

        if (content.TextSegments.Count != 1)
        {
            return false;
        }

        reasoningContent = content.TextSegments[0].Text;
        return true;
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

    public static (string? Code, string? Message) ReadError(string body, Action<JsonElement>? observe = null)
    {
        return TryReadError(body, out var code, out var message, observe)
            ? (code, message)
            : (null, null);
    }

    public static ProviderRequestUsage? ReadRequestUsage(JsonElement root, ProviderRequestUsage previous)
    {
        var usage = RequestUsageFields.Object(root, "usage");
        if (usage.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var prompt = RequestUsageFields.Object(usage, "prompt_tokens_details");
        var completion = RequestUsageFields.Object(usage, "completion_tokens_details");
        return new ProviderRequestUsage(
            0,
            RequestUsageFields.Count(usage, "prompt_tokens"),
            RequestUsageFields.Count(usage, "completion_tokens"),
            RequestUsageFields.Count(completion, "reasoning_tokens"),
            RequestUsageFields.Count(prompt, "cached_tokens") ?? RequestUsageFields.Count(usage, "prompt_cache_hit_tokens"),
            RequestUsageFields.Count(prompt, "cache_write_tokens"))
        {
            UncachedInputTokens = RequestUsageFields.Count(usage, "prompt_cache_miss_tokens")
        };
    }

    public static bool TryReadError(
        string body, out string? code, out string? message, Action<JsonElement>? observe = null)
    {
        code = null;
        message = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            observe?.Invoke(document.RootElement);
            return TryReadError(document.RootElement, out code, out message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryReadError(
        JsonElement root,
        out string? code,
        out string? message)
    {
        code = null;
        message = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            return TryReadErrorElement(error, out code, out message);
        }

        // Some gateways put the stream failure on the event itself instead of
        // nesting it under error.
        if (root.TryGetProperty("type", out var eventType)
            && eventType.ValueKind == JsonValueKind.String
            && string.Equals(eventType.GetString(), "error", StringComparison.OrdinalIgnoreCase))
        {
            code = ReadStringProperty(root, "code")
                ?? ReadStringProperty(root, "status")
                ?? ReadNumberProperty(root, "code");
            message = ReadStringProperty(root, "message");
            return true;
        }

        // Some gateways answer {"detail": "..."} or {"message": "..."} with no error object.
        message = ReadStringProperty(root, "detail") ?? ReadStringProperty(root, "message");
        return message is not null;
    }

    /// <summary>
    /// Describes a failed response whose body held no recognized error. The
    /// body is the provider's own text, so a short single-line excerpt is
    /// enough to show why the request was refused.
    /// </summary>
    public static string? DescribeBody(string body)
    {
        const int limit = 500;
        var text = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= limit ? text : text[..limit] + "...";
    }

    private static bool TryReadErrorElement(
        JsonElement error,
        out string? code,
        out string? message)
    {
        code = null;
        message = null;
        if (error.ValueKind == JsonValueKind.String)
        {
            message = error.GetString();
            return true;
        }

        if (error.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Gemini sends error.code as a number and the stable name in status.
        code = ReadStringProperty(error, "code")
            ?? ReadStringProperty(error, "type")
            ?? ReadStringProperty(error, "status")
            ?? ReadNumberProperty(error, "code");
        message = ReadStringProperty(error, "message");
        return true;
    }

    private static string? ReadNumberProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetRawText()
                : null;
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
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
