using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Ollama;

internal sealed class OllamaStreamParser : IProtocolStreamParser
{
    private int _nextItemIndex;
    private int? _reasoningItemIndex;
    private int? _textItemIndex;
    private bool _hasTools;

    public bool IsComplete { get; private set; }

    public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            throw new OllamaException(error.GetString() ?? "Ollama stream failed.");
        }

        var events = new List<ChatStreamEvent>();
        if (root.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("thinking", out var thinking)
                && thinking.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(thinking.GetString()))
            {
                _reasoningItemIndex ??= _nextItemIndex++;
                events.Add(new ChatReasoningTextDelta(
                    0,
                    _reasoningItemIndex.Value,
                    0,
                    ReasoningTextKind.Trace,
                    thinking.GetString()!));
            }

            if (message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(content.GetString()))
            {
                _textItemIndex ??= _nextItemIndex++;
                events.Add(new ChatTextDelta(
                    0,
                    _textItemIndex.Value,
                    ChatRole.Assistant,
                    content.GetString()!));
            }

            if (message.TryGetProperty("tool_calls", out var calls)
                && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    var tool = OllamaCodec.ReadToolCall(call);
                    events.Add(new ChatToolCallDelta(
                        0,
                        _nextItemIndex++,
                        tool.CallId,
                        tool.Name,
                        tool.Arguments));
                    _hasTools = true;
                }
            }
        }

        if (root.TryGetProperty("done", out var done) && done.GetBoolean())
        {
            if (IsComplete)
            {
                throw new OllamaException("Ollama completed a response more than once.");
            }

            var usage = OllamaCodec.ReadUsage(root);
            if (usage is not null)
            {
                events.Add(new ChatUsageReceived(usage));
            }

            events.Add(new ChatCandidateCompleted(
                0,
                _hasTools ? FinishReason.ToolCalls : FinishReason.Stop));
            IsComplete = true;
        }

        return events;
    }
}
