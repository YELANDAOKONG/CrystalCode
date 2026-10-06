using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Ollama;

internal sealed class OllamaCodec : IProtocolCodec
{
    public string Path => "api/chat";

    public bool UsesJsonLines => true;

    public void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public byte[] WriteRequest(ProtocolOptions options, ChatRequest request, bool stream)
    {
        JsonOutputGuard.Reject(request.JsonOutput, "Ollama");
        if (request.Items.Count == 0)
        {
            throw new ArgumentException("Ollama requires at least one transcript item.", nameof(request));
        }

        var messages = new JsonArray();
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        var reasoned = new HashSet<JsonObject>();
        foreach (var item in request.Items)
        {
            switch (item)
            {
                case ChatMessage message:
                    FlushPending(pending);
                    if (message.Role == ChatRole.Assistant)
                    {
                        var target = Assistant(messages);
                        target["content"] = (target["content"]?.GetValue<string>() ?? string.Empty)
                            + message.Text;
                    }
                    else
                    {
                        messages.Add(new JsonObject
                        {
                            ["role"] = message.Role.Value,
                            ["content"] = message.Text
                        });
                    }

                    break;
                case ChatReasoningItem reasoning:
                {
                    FlushPending(pending);
                    var reasoningMessage = Assistant(messages);
                    if (!reasoned.Add(reasoningMessage))
                    {
                        throw new NotSupportedException(
                            "Ollama accepts one reasoning block per assistant message.");
                    }

                    var thinking = string.Concat(
                        reasoning.Content.TextSegments.Select(static part => part.Text));
                    if (thinking.Length > 0)
                    {
                        reasoningMessage["thinking"] = thinking;
                    }

                    break;
                }
                case ToolCall call:
                    var args = JsonNode.Parse(call.Arguments) as JsonObject
                        ?? throw new JsonException("Ollama tool arguments must be a JSON object.");
                    var assistant = Assistant(messages);
                    var toolCalls = assistant["tool_calls"] as JsonArray;
                    if (toolCalls is null)
                    {
                        toolCalls = new JsonArray();
                        assistant["tool_calls"] = toolCalls;
                    }

                    toolCalls.Add(new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["index"] = toolCalls.Count,
                            ["name"] = call.Name,
                            ["arguments"] = args
                        }
                    });
                    callNames[call.CallId] = call.Name;
                    pending[call.CallId] = call.Name;
                    break;
                case ToolResult result:
                    if (!callNames.TryGetValue(result.CallId, out var name))
                    {
                        throw new NotSupportedException("Ollama tool result has no matching tool call.");
                    }

                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_name"] = name,
                        ["content"] = result.Text
                    });
                    pending.Remove(result.CallId);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Ollama does not support chat item type {item.GetType().Name}.");
            }
        }

        FlushPending(pending);
        var root = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = stream
        };
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description ?? string.Empty,
                        ["parameters"] = JsonNode.Parse(tool.InputSchema.GetRawText())
                    }
                });
            }

            root["tools"] = tools;
        }

        var generation = new JsonObject();
        if (options.Temperature is { } temperature)
        {
            generation["temperature"] = temperature;
        }

        if (options.TopP is { } topP)
        {
            generation["top_p"] = topP;
        }

        if (options.MaxTokens is { } maxTokens)
        {
            generation["num_predict"] = maxTokens;
        }

        if (generation.Count > 0)
        {
            root["options"] = generation;
        }

        WriteThinking(root, request.Reasoning);
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public ChatResponse ReadResponse(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message))
        {
            throw CreateException("Ollama response is missing a message.");
        }

        var items = ReadMessage(message).ToList();
        var hasTools = items.Any(static item => item is ToolCall);
        var finish = ReadFinish(root, hasTools);
        if (finish != FinishReason.ToolCalls)
        {
            items.RemoveAll(static item => item is ToolCall);
        }

        return new ChatResponse(
            [new ChatCandidate(items, finish)],
            ReadUsage(root));
    }

    public IProtocolStreamParser CreateStreamParser() => new OllamaStreamParser();

    public Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null) =>
        new OllamaException(message, statusCode, innerException, errorCode, retryAfter);

    internal static IReadOnlyList<ChatItem> ReadMessage(JsonElement message)
    {
        var items = new List<ChatItem>();
        if (message.TryGetProperty("thinking", out var thinking)
            && thinking.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(thinking.GetString()))
        {
            items.Add(new ChatReasoningItem(new ReasoningContent(
                [new ReasoningText(thinking.GetString()!, ReasoningTextKind.Trace)])));
        }

        if (message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(content.GetString()))
        {
            items.Add(new ChatMessage(ChatRole.Assistant, content.GetString()!));
        }

        if (message.TryGetProperty("tool_calls", out var calls)
            && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                items.Add(ReadToolCall(call));
            }
        }

        return items;
    }

    internal static ToolCall ReadToolCall(JsonElement call)
    {
        var function = call.GetProperty("function");
        var name = function.GetProperty("name").GetString() ?? string.Empty;
        var arguments = function.TryGetProperty("arguments", out var args)
            ? args.GetRawText()
            : "{}";
        return new ToolCall($"ollama_{Guid.NewGuid():N}", name, arguments);
    }

    internal static FinishReason ReadFinish(JsonElement root, bool hasTools)
    {
        var reason = root.TryGetProperty("done_reason", out var done)
            && done.ValueKind == JsonValueKind.String
                ? done.GetString()
                : null;
        if (reason == "length")
        {
            return FinishReason.Length;
        }

        if (hasTools && reason is null or "stop")
        {
            return FinishReason.ToolCalls;
        }

        if (reason is null or "stop")
        {
            return FinishReason.Stop;
        }

        return new FinishReason(reason);
    }

    internal static TokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("prompt_eval_count", out var input)
            || !root.TryGetProperty("eval_count", out var output))
        {
            return null;
        }

        return new TokenUsage(input.GetInt64(), output.GetInt64());
    }

    private static JsonObject Assistant(JsonArray messages)
    {
        if (messages.Count > 0
            && messages[^1] is JsonObject last
            && last["role"]?.GetValue<string>() == "assistant")
        {
            return last;
        }

        var assistant = new JsonObject { ["role"] = "assistant", ["content"] = string.Empty };
        messages.Add(assistant);
        return assistant;
    }

    private static void FlushPending(Dictionary<string, string> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var name = string.Empty;
        foreach (var pendingName in pending.Values)
        {
            name = pendingName;
            break;
        }

        throw new NotSupportedException($"Ollama is missing a tool result for '{name}'.");
    }

    private static void WriteThinking(JsonObject root, ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return;
        }

        if (reasoning.TokenBudget is not null)
        {
            throw new NotSupportedException("Ollama does not support a reasoning token budget.");
        }

        if (reasoning.Mode == ReasoningMode.Disabled || reasoning.Output == ReasoningOutput.None)
        {
            root["think"] = false;
        }
        else if (reasoning.Effort is { } effort)
        {
            root["think"] = effort == ReasoningEffort.Maximum ? "high" : effort.Value;
        }
        else if (reasoning.Mode == ReasoningMode.Enabled)
        {
            root["think"] = true;
        }
    }
}
