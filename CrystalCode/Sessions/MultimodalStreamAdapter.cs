using Crystal;
using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Reasoning;

namespace CrystalCode.Sessions;

/// <summary>Maps the currently supported text-only model output to the live UI.</summary>
public static class MultimodalStreamAdapter
{
    public static ChatStreamEvent? Convert(MultimodalChatStreamEvent streamEvent) =>
        streamEvent switch
        {
            MultimodalMessageStarted => null,
            MultimodalMessageTextDelta text => new ChatTextDelta(
                text.CandidateIndex,
                text.ItemIndex,
                ChatRole.Assistant,
                text.Text),
            MultimodalMessageContentReceived
            {
                Content: TextContent text
            } content => new ChatTextDelta(
                content.CandidateIndex,
                content.ItemIndex,
                ChatRole.Assistant,
                text.Text),
            MultimodalMessageContentReceived content => throw UnsupportedOutput(
                content.Content.Modality),
            MultimodalReasoningTextDelta reasoning => new ChatReasoningTextDelta(
                reasoning.CandidateIndex,
                reasoning.ItemIndex,
                reasoning.PartIndex,
                ToReasoningKind(reasoning.Kind),
                reasoning.Text),
            MultimodalReasoningContentReceived
            {
                Content: TextContent text
            } reasoning => new ChatReasoningTextDelta(
                reasoning.CandidateIndex,
                reasoning.ItemIndex,
                reasoning.PartIndex,
                ToReasoningKind(reasoning.Kind),
                text.Text),
            MultimodalReasoningContentReceived reasoning => throw UnsupportedOutput(
                reasoning.Content.Modality),
            MultimodalReasoningStateReceived reasoning =>
                new ChatReasoningStateReceived(
                    reasoning.CandidateIndex,
                    reasoning.ItemIndex,
                    reasoning.State),
            MultimodalToolCallDelta tool => new ChatToolCallDelta(
                tool.CandidateIndex,
                tool.ItemIndex,
                tool.CallIdDelta,
                tool.NameDelta,
                tool.ArgumentsDelta),
            MultimodalToolCallContentReceived tool => throw UnsupportedOutput(
                tool.Content.Modality),
            MultimodalChatCandidateCompleted completed => new ChatCandidateCompleted(
                completed.CandidateIndex,
                completed.FinishReason),
            MultimodalChatUsageReceived usage => new ChatUsageReceived(usage.Usage),
            _ => throw new NotSupportedException(
                $"Multimodal stream event {streamEvent.GetType().Name} is not supported.")
        };

    private static ReasoningTextKind ToReasoningKind(
        MultimodalReasoningKind kind) =>
        kind == MultimodalReasoningKind.Trace
            ? ReasoningTextKind.Trace
            : ReasoningTextKind.Summary;

    private static NotSupportedException UnsupportedOutput(ContentModality modality) =>
        new($"Model {modality.Value} output is not supported yet.");
}
