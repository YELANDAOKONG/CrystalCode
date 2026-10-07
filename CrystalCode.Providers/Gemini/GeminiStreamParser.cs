using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Gemini;

internal sealed class GeminiStreamParser : IProtocolStreamParser
{
    private readonly Dictionary<int, CandidateState> _candidates = [];
    private TokenUsage? _usage;

    public bool IsComplete => _candidates.Count > 0
        && _candidates.Values.All(static candidate => candidate.Completed);

    public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
    {
        var events = new List<ChatStreamEvent>();
        var completeBefore = IsComplete;
        if (root.TryGetProperty("error", out var error))
        {
            throw new GeminiException(error.TryGetProperty("message", out var message)
                ? message.GetString() ?? "Gemini stream failed."
                : "Gemini stream failed.");
        }

        if (root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                var index = candidate.TryGetProperty("index", out var candidateIndex)
                    ? candidateIndex.GetInt32()
                    : 0;
                if (!_candidates.TryGetValue(index, out var state))
                {
                    state = new CandidateState();
                    _candidates.Add(index, state);
                }

                if (candidate.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        ReadPart(index, state, part, events);
                    }
                }

                if (candidate.TryGetProperty("finishReason", out var finish)
                    && finish.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(finish.GetString()))
                {
                    if (state.Completed)
                    {
                        throw new GeminiException("Gemini completed a candidate more than once.");
                    }

                    var finishReason = GeminiCodec.ReadFinish(candidate, state.HasTools);
                    if (finishReason == FinishReason.ToolCalls)
                    {
                        events.AddRange(state.HeldToolEvents);
                    }

                    state.HeldToolEvents.Clear();
                    state.Completed = true;
                    events.Add(new ChatCandidateCompleted(index, finishReason));
                }
            }
        }

        // Each chunk repeats the cumulative usage. Keep the latest totals and
        // report them once, when every candidate has completed.
        if (GeminiCodec.ReadUsage(root) is { } usage)
        {
            _usage = usage;
        }

        if (!completeBefore && IsComplete && _usage is not null)
        {
            events.Add(new ChatUsageReceived(_usage));
        }

        return events;
    }

    private static void ReadPart(
        int candidateIndex,
        CandidateState state,
        JsonElement part,
        List<ChatStreamEvent> events)
    {
        var hasSignature = part.TryGetProperty("thoughtSignature", out _);
        var opaque = hasSignature
            ? new OpaqueReasoningState(
                GeminiCodec.PartStateFormat,
                Encoding.UTF8.GetBytes(part.GetRawText()))
            : null;
        if (part.TryGetProperty("functionCall", out var function))
        {
            // The finish reason decides whether this call is a tool request.
            // Hold it, and its signature, until that reason arrives.
            ResetText(state);
            state.HasTools = true;
            var itemIndex = state.NextItemIndex++;
            var id = function.TryGetProperty("id", out var identifier)
                ? identifier.GetString()
                : null;
            id ??= $"gemini_{Guid.NewGuid():N}";
            var name = function.GetProperty("name").GetString() ?? string.Empty;
            var args = function.TryGetProperty("args", out var arguments)
                ? arguments.GetRawText()
                : "{}";
            state.HeldToolEvents.Add(new ChatToolCallDelta(candidateIndex, itemIndex, id, name, args));
            if (opaque is not null)
            {
                state.HeldToolEvents.Add(new ChatReasoningStateReceived(
                    candidateIndex,
                    state.NextItemIndex++,
                    opaque));
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
            opaque ??= new OpaqueReasoningState(
                GeminiCodec.PartStateFormat,
                Encoding.UTF8.GetBytes(part.GetRawText()));
            ResetText(state);
            var itemIndex = state.NextItemIndex++;
            events.Add(new ChatReasoningTextDelta(
                candidateIndex,
                itemIndex,
                0,
                ReasoningTextKind.Summary,
                value));
            if (opaque is not null)
            {
                events.Add(new ChatReasoningStateReceived(candidateIndex, itemIndex, opaque));
            }

            return;
        }

        var signature = hasSignature
            ? part.GetProperty("thoughtSignature").GetString() ?? string.Empty
            : null;
        if (value.Length == 0)
        {
            if (opaque is not null)
            {
                ResetText(state);
                events.Add(new ChatReasoningStateReceived(
                    candidateIndex,
                    state.NextItemIndex++,
                    opaque));
            }

            return;
        }

        var textIndex = state.ActiveTextIndex ??= state.NextItemIndex++;
        state.PartText.Append(value);
        events.Add(new ChatTextDelta(candidateIndex, textIndex, ChatRole.Assistant, value));
        if (signature is not null)
        {
            events.Add(new ChatReasoningStateReceived(
                candidateIndex,
                state.NextItemIndex++,
                SignedText(state.PartText.ToString(), signature)));
            ResetText(state);
        }
    }

    private static void ResetText(CandidateState state)
    {
        state.ActiveTextIndex = null;
        state.PartText.Clear();
    }

    private static OpaqueReasoningState SignedText(string text, string signature)
    {
        var part = JsonSerializer.SerializeToUtf8Bytes(new
        {
            text,
            thoughtSignature = signature
        });
        return new OpaqueReasoningState(GeminiCodec.PartStateFormat, part);
    }

    private sealed class CandidateState
    {
        public int NextItemIndex { get; set; }
        public int? ActiveTextIndex { get; set; }
        public StringBuilder PartText { get; } = new();
        public List<ChatStreamEvent> HeldToolEvents { get; } = [];
        public bool HasTools { get; set; }
        public bool Completed { get; set; }
    }
}
