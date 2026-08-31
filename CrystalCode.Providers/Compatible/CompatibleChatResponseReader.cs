using System.Text.Json;

using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

namespace CrystalCode.Providers.Compatible;

internal static class CompatibleChatResponseReader
{
    public static ChatResponse Read(CompatibleProfile profile, JsonElement root)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} chat response contained no choices.");
        }

        var candidates = new ChatCandidate[choices.GetArrayLength()];
        var index = 0;
        foreach (var choice in choices.EnumerateArray())
        {
            candidates[index] = ReadCandidate(profile, choice);
            index++;
        }

        return new ChatResponse(candidates, CompatibleWire.ReadUsage(root, profile.Faults));
    }

    private static ChatCandidate ReadCandidate(CompatibleProfile profile, JsonElement choice)
    {
        if (!choice.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} chat choice is missing a message.");
        }

        if (!CompatibleWire.TryReadString(choice, "finish_reason", out var finishReason)
            || string.IsNullOrWhiteSpace(finishReason))
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} chat choice is missing a finish reason.");
        }

        return new ChatCandidate(
            ReadItems(profile, message),
            CompatibleWire.ReadFinishReason(finishReason));
    }

    private static List<ChatItem> ReadItems(CompatibleProfile profile, JsonElement message)
    {
        var items = new List<ChatItem>();

        if (CompatibleWire.TryReadString(message, "reasoning_content", out var reasoningContent)
            && !string.IsNullOrEmpty(reasoningContent))
        {
            OpaqueReasoningState? state = null;
            if (profile.ReasoningStateFormat is not null)
            {
                state = CompatibleWire.CreateReasoningState(
                    profile.ReasoningStateFormat,
                    reasoningContent);
            }

            items.Add(
                new ChatReasoningItem(
                    new ReasoningContent(
                        [new ReasoningText(reasoningContent, ReasoningTextKind.Trace)],
                        state)));
        }

        if (CompatibleWire.TryReadString(message, "content", out var content)
            && content is not null)
        {
            items.Add(new ChatMessage(ChatRole.Assistant, content));
        }

        if (message.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind != JsonValueKind.Null)
        {
            if (toolCalls.ValueKind != JsonValueKind.Array)
            {
                throw profile.Faults.Create(
                    $"{profile.VendorName} tool_calls must be a JSON array.");
            }

            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                items.Add(ReadToolCall(profile, toolCall));
            }
        }

        if (items.Count == 0)
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} chat message contained no text, reasoning, or tool calls.");
        }

        return items;
    }

    private static ToolCall ReadToolCall(CompatibleProfile profile, JsonElement toolCall)
    {
        if (CompatibleWire.TryReadString(toolCall, "type", out var toolType)
            && toolType is not null
            && !string.Equals(toolType, "function", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"{profile.VendorName} tool type '{toolType}' is not supported.");
        }

        if (!CompatibleWire.TryReadString(toolCall, "id", out var callId)
            || string.IsNullOrWhiteSpace(callId))
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} tool call is missing an identifier.");
        }

        if (!toolCall.TryGetProperty("function", out var function)
            || function.ValueKind != JsonValueKind.Object)
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} tool call is missing a function.");
        }

        if (!CompatibleWire.TryReadString(function, "name", out var name)
            || string.IsNullOrWhiteSpace(name))
        {
            throw profile.Faults.Create($"{profile.VendorName} tool call is missing a name.");
        }

        if (!CompatibleWire.TryReadString(function, "arguments", out var arguments)
            || arguments is null)
        {
            throw profile.Faults.Create(
                $"{profile.VendorName} tool call is missing argument text.");
        }

        return new ToolCall(callId, name, arguments);
    }
}
