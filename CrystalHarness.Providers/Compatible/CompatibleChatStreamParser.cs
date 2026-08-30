using System.Text;
using System.Text.Json;

using Crystal.Chat;
using Crystal.Reasoning;

namespace CrystalHarness.Providers.Compatible;

internal sealed class CompatibleChatStreamParser
{
    private readonly CompatibleProfile _profile;
    private readonly Dictionary<int, CandidateAssembly> _candidates = [];
    private bool _usageReceived;

    public CompatibleChatStreamParser(CompatibleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
    }

    public bool IsComplete =>
        _candidates.Count > 0
        && _candidates.Values.All(static candidate => candidate.Completed);

    public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
    {
        var events = new List<ChatStreamEvent>();

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                ParseChoice(choice, events);
            }
        }

        var usage = CompatibleWire.ReadUsage(root, _profile.Faults);
        if (usage is not null)
        {
            if (_usageReceived)
            {
                throw _profile.Faults.Create(
                    $"{_profile.VendorName} chat stream reported usage more than once.");
            }

            _usageReceived = true;
            events.Add(new ChatUsageReceived(usage));
        }

        return events;
    }

    private void ParseChoice(JsonElement choice, List<ChatStreamEvent> events)
    {
        if (!CompatibleWire.TryReadInt64(choice, "index", out var rawIndex))
        {
            rawIndex = 0;
        }

        var candidateIndex = checked((int)rawIndex);
        var assembly = GetOrAdd(candidateIndex);

        if (choice.TryGetProperty("delta", out var delta)
            && delta.ValueKind == JsonValueKind.Object)
        {
            ParseDelta(assembly, delta, events);
        }

        if (CompatibleWire.TryReadString(choice, "finish_reason", out var finishReason)
            && !string.IsNullOrWhiteSpace(finishReason))
        {
            if (assembly.Completed)
            {
                throw _profile.Faults.Create(
                    $"{_profile.VendorName} chat stream completed a candidate more than once.");
            }

            assembly.Completed = true;
            if (assembly.Reasoning.Length > 0
                && assembly.ReasoningItemIndex is { } reasoningIndex
                && _profile.ReasoningStateFormat is not null)
            {
                events.Add(
                    new ChatReasoningStateReceived(
                        candidateIndex,
                        reasoningIndex,
                        CompatibleWire.CreateReasoningState(
                            _profile.ReasoningStateFormat,
                            assembly.Reasoning.ToString())));
            }

            events.Add(
                new ChatCandidateCompleted(
                    candidateIndex,
                    CompatibleWire.ReadFinishReason(finishReason)));
        }
    }

    private static void ParseDelta(
        CandidateAssembly assembly,
        JsonElement delta,
        List<ChatStreamEvent> events)
    {
        if (CompatibleWire.TryReadString(delta, "reasoning_content", out var reasoning)
            && reasoning is not null)
        {
            if (reasoning.Length > 0 || assembly.ReasoningItemIndex is not null)
            {
                assembly.ReasoningItemIndex ??= assembly.NextItemIndex++;
                assembly.Reasoning.Append(reasoning);
                events.Add(
                    new ChatReasoningTextDelta(
                        assembly.CandidateIndex,
                        assembly.ReasoningItemIndex.Value,
                        textSegmentIndex: 0,
                        ReasoningTextKind.Trace,
                        reasoning));
            }
        }

        if (CompatibleWire.TryReadString(delta, "content", out var content)
            && content is not null)
        {
            var isTrailingEmptyContent = content.Length == 0
                && assembly.ContentItemIndex is null
                && assembly.ToolCallItemIndexes.Count > 0;
            if (!isTrailingEmptyContent)
            {
                assembly.ContentItemIndex ??= assembly.NextItemIndex++;
                events.Add(
                    new ChatTextDelta(
                        assembly.CandidateIndex,
                        assembly.ContentItemIndex.Value,
                        ChatRole.Assistant,
                        content));
            }
        }

        if (delta.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                ParseToolCallDelta(assembly, toolCall, events);
            }
        }
    }

    private static void ParseToolCallDelta(
        CandidateAssembly assembly,
        JsonElement toolCall,
        List<ChatStreamEvent> events)
    {
        if (!CompatibleWire.TryReadInt64(toolCall, "index", out var rawIndex))
        {
            rawIndex = 0;
        }

        var providerIndex = checked((int)rawIndex);
        if (!assembly.ToolCallItemIndexes.TryGetValue(providerIndex, out var itemIndex))
        {
            itemIndex = assembly.NextItemIndex++;
            assembly.ToolCallItemIndexes[providerIndex] = itemIndex;
        }

        var callIdDelta = ReadDeltaString(toolCall, "id");
        var nameDelta = "";
        var argumentsDelta = "";
        if (toolCall.TryGetProperty("function", out var function)
            && function.ValueKind == JsonValueKind.Object)
        {
            nameDelta = ReadDeltaString(function, "name");
            argumentsDelta = ReadDeltaString(function, "arguments");
        }

        events.Add(
            new ChatToolCallDelta(
                assembly.CandidateIndex,
                itemIndex,
                callIdDelta,
                nameDelta,
                argumentsDelta));
    }

    private static string ReadDeltaString(JsonElement element, string name)
    {
        if (!CompatibleWire.TryReadString(element, name, out var value) || value is null)
        {
            return "";
        }

        return value;
    }

    private CandidateAssembly GetOrAdd(int candidateIndex)
    {
        if (_candidates.TryGetValue(candidateIndex, out var assembly))
        {
            return assembly;
        }

        assembly = new CandidateAssembly(candidateIndex);
        _candidates[candidateIndex] = assembly;
        return assembly;
    }

    private sealed class CandidateAssembly
    {
        public CandidateAssembly(int candidateIndex)
        {
            CandidateIndex = candidateIndex;
        }

        public int CandidateIndex { get; }

        public int NextItemIndex { get; set; }

        public int? ReasoningItemIndex { get; set; }

        public int? ContentItemIndex { get; set; }

        public StringBuilder Reasoning { get; } = new();

        public Dictionary<int, int> ToolCallItemIndexes { get; } = [];

        public bool Completed { get; set; }
    }
}
