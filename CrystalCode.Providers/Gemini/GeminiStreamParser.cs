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
    private bool _usageReceived;

    public bool IsComplete => _candidates.Count > 0
        && _candidates.Values.All(static candidate => candidate.Completed);

    public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
    {
        var events = new List<ChatStreamEvent>();
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

                if (candidate.TryGetProperty("finishReason", out _))
                {
                    if (state.Completed)
                    {
                        throw new GeminiException("Gemini completed a candidate more than once.");
                    }

                    state.Completed = true;
                    events.Add(new ChatCandidateCompleted(
                        index,
                        GeminiCodec.ReadFinish(candidate, state.HasTools)));
                }
            }
        }

        var usage = GeminiCodec.ReadUsage(root);
        if (usage is not null && !_usageReceived)
        {
            _usageReceived = true;
            events.Add(new ChatUsageReceived(usage));
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
            state.ActiveTextIndex = null;
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
            events.Add(new ChatToolCallDelta(candidateIndex, itemIndex, id, name, args));
            if (opaque is not null)
            {
                events.Add(new ChatReasoningStateReceived(
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
            state.ActiveTextIndex = null;
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

        if (hasSignature)
        {
            state.ActiveTextIndex = null;
        }

        if (value.Length == 0)
        {
            if (opaque is not null)
            {
                events.Add(new ChatReasoningStateReceived(
                    candidateIndex,
                    state.NextItemIndex++,
                    opaque));
            }

            return;
        }

        var textIndex = state.ActiveTextIndex ??= state.NextItemIndex++;
        events.Add(new ChatTextDelta(candidateIndex, textIndex, ChatRole.Assistant, value));
        if (opaque is not null)
        {
            events.Add(new ChatReasoningStateReceived(
                candidateIndex,
                state.NextItemIndex++,
                opaque));
            state.ActiveTextIndex = null;
        }
    }

    private sealed class CandidateState
    {
        public int NextItemIndex { get; set; }
        public int? ActiveTextIndex { get; set; }
        public bool HasTools { get; set; }
        public bool Completed { get; set; }
    }
}
