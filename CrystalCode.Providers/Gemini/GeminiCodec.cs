using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Gemini;

internal sealed class GeminiCodec : IProtocolCodec
{
    internal const string PartStateFormat = "gemini.part";
    private readonly string _model;

    public GeminiCodec(string model) => _model = model;

    public string Path => $"models/{Uri.EscapeDataString(_model)}:generateContent";

    public string GetPath(bool stream) => stream
        ? $"models/{Uri.EscapeDataString(_model)}:streamGenerateContent?alt=sse"
        : Path;

    public void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("x-goog-api-key", apiKey);
        }
    }

    public byte[] WriteRequest(ProtocolOptions options, ChatRequest request, bool stream)
    {
        if (request.Items.Count == 0)
        {
            throw new ArgumentException("Gemini requires at least one transcript item.", nameof(request));
        }

        var contents = new JsonArray();
        var systemParts = new JsonArray();
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonObject? lastPart = null;
        foreach (var item in request.Items)
        {
            switch (item)
            {
                case ChatMessage message when message.Role == ChatRole.System:
                    systemParts.Add(new JsonObject { ["text"] = message.Text });
                    lastPart = null;
                    break;
                case ChatMessage message:
                    FlushPending(contents, pending);
                    lastPart = new JsonObject { ["text"] = message.Text };
                    AddPart(contents, message.Role == ChatRole.Assistant ? "model" : "user", lastPart);
                    break;
                case ToolCall call:
                    var arguments = JsonNode.Parse(call.Arguments) as JsonObject
                        ?? throw new JsonException("Gemini tool arguments must be a JSON object.");
                    lastPart = new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["args"] = arguments
                        }
                    };
                    if (!IsSyntheticCallId(call.CallId))
                    {
                        lastPart["functionCall"]!["id"] = call.CallId;
                    }
                    AddPart(contents, "model", lastPart);
                    callNames[call.CallId] = call.Name;
                    pending[call.CallId] = call.Name;
                    break;
                case ToolResult result:
                    if (!callNames.TryGetValue(result.CallId, out var name))
                    {
                        throw new NotSupportedException(
                            "Gemini tool result has no matching tool call.");
                    }

                    lastPart = CreateResult(result.CallId, name, result.Text,
                        result.Status == ToolResultStatus.Failure);
                    AddPart(contents, "user", lastPart);
                    pending.Remove(result.CallId);
                    break;
                case ChatReasoningItem reasoning when reasoning.Content.State?.Format == PartStateFormat:
                    var raw = JsonNode.Parse(reasoning.Content.State.Data.Span) as JsonObject
                        ?? throw new JsonException("Gemini part state is invalid.");
                    if (raw["thought"]?.GetValue<bool>() == true)
                    {
                        AddPart(contents, "model", raw);
                    }
                    else if (lastPart is not null && SamePart(lastPart, raw))
                    {
                        var parts = (JsonArray)((JsonObject)contents[^1]!)["parts"]!;
                        parts[^1] = raw;
                    }
                    else
                    {
                        AddPart(contents, "model", raw);
                    }

                    lastPart = null;
                    break;
                case ChatReasoningItem:
                    lastPart = null;
                    break;
                default:
                    throw new NotSupportedException(
                        $"Gemini does not support chat item type {item.GetType().Name}.");
            }
        }

        FlushPending(contents, pending);
        var root = new JsonObject { ["contents"] = contents };
        if (systemParts.Count > 0)
        {
            root["systemInstruction"] = new JsonObject { ["parts"] = systemParts };
        }

        if (request.Tools.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var tool in request.Tools)
            {
                declarations.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description ?? string.Empty,
                    ["parametersJsonSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText())
                });
            }

            root["tools"] = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = declarations
            });
        }

        var generation = new JsonObject();
        if (options.Temperature is { } temperature)
        {
            generation["temperature"] = temperature;
        }

        if (options.TopP is { } topP)
        {
            generation["topP"] = topP;
        }

        if (options.MaxTokens is { } maxTokens)
        {
            generation["maxOutputTokens"] = maxTokens;
        }

        WriteReasoning(generation, request.Reasoning);
        if (generation.Count > 0)
        {
            root["generationConfig"] = generation;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public ChatResponse ReadResponse(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw CreateException("Gemini response contained no candidates.");
        }

        var result = new List<ChatCandidate>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            var items = new List<ChatItem>();
            if (candidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    ReadPart(part, items);
                }
            }

            result.Add(new ChatCandidate(items, ReadFinish(
                candidate,
                items.Any(static item => item is ToolCall))));
        }

        return new ChatResponse(result, ReadUsage(root));
    }

    public IProtocolStreamParser CreateStreamParser() => new GeminiStreamParser();

    public Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null) =>
        new GeminiException(message, statusCode, innerException, errorCode, retryAfter);

    internal static void AddPart(JsonArray contents, string role, JsonObject part)
    {
        if (contents.Count > 0
            && ((JsonObject)contents[^1]!)["role"]?.GetValue<string>() == role)
        {
            ((JsonArray)((JsonObject)contents[^1]!)["parts"]!).Add(part);
            return;
        }

        contents.Add(new JsonObject
        {
            ["role"] = role,
            ["parts"] = new JsonArray(part)
        });
    }

    internal static JsonObject CreateResult(string callId, string name, string text, bool failed)
    {
        var response = new JsonObject
        {
            ["functionResponse"] = new JsonObject
            {
                ["name"] = name,
                ["response"] = new JsonObject { [failed ? "error" : "result"] = text }
            }
        };
        if (!IsSyntheticCallId(callId))
        {
            response["functionResponse"]!["id"] = callId;
        }

        return response;
    }

    private static bool IsSyntheticCallId(string callId) =>
        callId.StartsWith("gemini_", StringComparison.Ordinal);

    private static bool SamePart(JsonObject current, JsonObject replay)
    {
        if (current["functionCall"] is not null && replay["functionCall"] is not null)
        {
            return true;
        }

        return current["text"] is JsonValue currentText
            && replay["text"] is JsonValue replayText
            && currentText.GetValue<string>() == replayText.GetValue<string>();
    }

    internal static void ReadPart(JsonElement part, List<ChatItem> items)
    {
        if (part.TryGetProperty("functionCall", out var function))
        {
            var name = function.GetProperty("name").GetString() ?? string.Empty;
            var id = function.TryGetProperty("id", out var identifier)
                ? identifier.GetString()
                : null;
            id ??= $"gemini_{Guid.NewGuid():N}";
            var callState = part.TryGetProperty("thoughtSignature", out _)
                ? new OpaqueReasoningState(
                    PartStateFormat,
                    Encoding.UTF8.GetBytes(part.GetRawText()))
                : null;
            var args = function.TryGetProperty("args", out var arguments)
                ? arguments.GetRawText()
                : "{}";
            items.Add(new ToolCall(id, name, args));
            if (callState is not null)
            {
                items.Add(new ChatReasoningItem(new ReasoningContent([], callState)));
            }

            return;
        }

        if (!part.TryGetProperty("text", out var text))
        {
            return;
        }

        var value = text.GetString() ?? string.Empty;
        if (part.TryGetProperty("thought", out var thought) && thought.GetBoolean())
        {
            var thoughtState = new OpaqueReasoningState(
                PartStateFormat,
                Encoding.UTF8.GetBytes(part.GetRawText()));
            items.Add(new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText(value, ReasoningTextKind.Summary)],
                thoughtState)));
            return;
        }

        var state = part.TryGetProperty("thoughtSignature", out _)
            ? new OpaqueReasoningState(PartStateFormat, Encoding.UTF8.GetBytes(part.GetRawText()))
            : null;
        if (value.Length > 0)
        {
            items.Add(new ChatMessage(ChatRole.Assistant, value));
        }

        if (state is not null)
        {
            items.Add(new ChatReasoningItem(new ReasoningContent([], state)));
        }
    }

    internal static FinishReason ReadFinish(JsonElement candidate, bool hasTools)
    {
        if (hasTools)
        {
            return FinishReason.ToolCalls;
        }

        var reason = candidate.TryGetProperty("finishReason", out var finish)
            ? finish.GetString()
            : null;
        return reason switch
        {
            "MAX_TOKENS" => FinishReason.Length,
            "SAFETY" or "RECITATION" => FinishReason.ContentFilter,
            _ => FinishReason.Stop
        };
    }

    internal static TokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
        {
            return null;
        }

        var input = usage.TryGetProperty("promptTokenCount", out var prompt)
            ? prompt.GetInt64()
            : 0;
        var output = usage.TryGetProperty("candidatesTokenCount", out var candidates)
            ? candidates.GetInt64()
            : 0;
        var thoughts = usage.TryGetProperty("thoughtsTokenCount", out var reasoning)
            ? reasoning.GetInt64()
            : 0;
        return new TokenUsage(input, output + thoughts, thoughts);
    }

    private static void FlushPending(JsonArray contents, Dictionary<string, string> pending)
    {
        foreach (var (id, name) in pending)
        {
            AddPart(contents, "user", CreateResult(id, name, "Tool execution was cancelled.", true));
        }

        pending.Clear();
    }

    private void WriteReasoning(JsonObject generation, ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return;
        }

        if (reasoning.TokenBudget is not null)
        {
            throw new NotSupportedException("Gemini does not support a reasoning token budget here.");
        }

        if (reasoning.Mode == ReasoningMode.Disabled || reasoning.Output == ReasoningOutput.None)
        {
            if (_model.StartsWith("gemini-3", StringComparison.Ordinal))
            {
                throw new NotSupportedException("Gemini 3 thinking cannot be disabled.");
            }

            generation["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = 0 };
            return;
        }

        if (reasoning.Effort is not { } effort)
        {
            return;
        }

        var level = effort == ReasoningEffort.Maximum ? "high" : effort.Value;
        if (_model.StartsWith("gemini-2.5", StringComparison.Ordinal))
        {
            var budget = level switch
            {
                "minimal" or "low" => 1024,
                "medium" => 8192,
                "high" => 24576,
                _ => throw new NotSupportedException("Gemini reasoning effort is unsupported.")
            };
            generation["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = budget };
        }
        else
        {
            if (level == "minimal")
            {
                throw new NotSupportedException("This Gemini model does not support minimal thinking.");
            }

            generation["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = level.ToUpperInvariant() };
        }
    }
}
