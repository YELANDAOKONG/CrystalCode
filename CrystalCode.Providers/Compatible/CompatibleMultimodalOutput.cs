using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Providers.Compatible;

internal sealed class CompatibleMultimodalOutput
{
    private readonly HashSet<int> _startedMessages = [];

    public static MultimodalChatResponse Convert(ChatResponse response) =>
        new(
            response.Candidates.Select(static candidate =>
                new MultimodalChatCandidate(
                    candidate.Items.Select(ConvertItem),
                    candidate.FinishReason)),
            response.Usage);

    public IReadOnlyList<MultimodalChatStreamEvent> Convert(
        ChatStreamEvent streamEvent)
    {
        var events = new List<MultimodalChatStreamEvent>();
        if (streamEvent is ChatTextDelta text
            && _startedMessages.Add(text.ItemIndex))
        {
            events.Add(new MultimodalMessageStarted(
                text.CandidateIndex,
                text.ItemIndex,
                MultimodalChatRole.Assistant));
        }

        events.Add(ConvertEvent(streamEvent));
        return events;
    }

    private static MultimodalChatItem ConvertItem(ChatItem item) => item switch
    {
        ChatMessage message => new MultimodalMessage(
            new MultimodalChatRole(message.Role.Value),
            [new TextContent(message.Text)]),
        ChatReasoningItem reasoning => new MultimodalReasoningItem(
            new MultimodalReasoningContent(
                reasoning.Content.TextSegments.Select(static text =>
                    new MultimodalReasoningPart(
                        new TextContent(text.Text),
                        text.Kind == Crystal.Reasoning.ReasoningTextKind.Trace
                            ? MultimodalReasoningKind.Trace
                            : MultimodalReasoningKind.Summary)),
                reasoning.Content.State)),
        ToolCall call => new MultimodalToolCall(
            call.CallId,
            call.Name,
            call.Arguments),
        ToolResult result => new MultimodalToolResult(
            result.CallId,
            [new TextContent(result.Text)],
            result.Status == ToolResultStatus.Success
                ? MultimodalToolResultStatus.Success
                : MultimodalToolResultStatus.Failure),
        _ => throw new NotSupportedException(
            $"Compatible Chat Completions does not support {item.GetType().Name}.")
    };

    private static MultimodalChatStreamEvent ConvertEvent(
        ChatStreamEvent streamEvent) => streamEvent switch
    {
        ChatTextDelta text => new MultimodalMessageTextDelta(
            text.CandidateIndex,
            text.ItemIndex,
            0,
            text.Text),
        ChatReasoningTextDelta reasoning => new MultimodalReasoningTextDelta(
            reasoning.CandidateIndex,
            reasoning.ItemIndex,
            reasoning.TextSegmentIndex,
            reasoning.Kind == Crystal.Reasoning.ReasoningTextKind.Trace
                ? MultimodalReasoningKind.Trace
                : MultimodalReasoningKind.Summary,
            reasoning.Text),
        ChatReasoningStateReceived reasoning =>
            new MultimodalReasoningStateReceived(
                reasoning.CandidateIndex,
                reasoning.ItemIndex,
                reasoning.State),
        ChatToolCallDelta tool => new MultimodalToolCallDelta(
            tool.CandidateIndex,
            tool.ItemIndex,
            tool.CallIdDelta,
            tool.NameDelta,
            tool.ArgumentsDelta),
        ChatCandidateCompleted completed => new MultimodalChatCandidateCompleted(
            completed.CandidateIndex,
            completed.FinishReason),
        ChatUsageReceived usage => new MultimodalChatUsageReceived(usage.Usage),
        _ => throw new NotSupportedException(
            $"Compatible Chat Completions does not support stream event {streamEvent.GetType().Name}.")
    };
}
