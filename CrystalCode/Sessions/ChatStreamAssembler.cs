using System.Text;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Assembles a <see cref="ChatResponse"/> from one streamed model round.
/// </summary>
public sealed class ChatStreamAssembler
{
    private readonly Dictionary<int, CandidateBuffer> _candidates = [];
    private TokenUsage? _usage;

    public void Apply(ChatStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);
        switch (streamEvent)
        {
            case ChatReasoningTextDelta reasoning:
                GetCandidate(reasoning.CandidateIndex)
                    .GetItem(reasoning.ItemIndex)
                    .AppendReasoning(reasoning.TextSegmentIndex, reasoning.Kind, reasoning.Text);
                break;
            case ChatTextDelta text:
                GetCandidate(text.CandidateIndex)
                    .GetItem(text.ItemIndex)
                    .AppendText(text.Role, text.Text);
                break;
            case ChatToolCallDelta toolCall:
                GetCandidate(toolCall.CandidateIndex)
                    .GetItem(toolCall.ItemIndex)
                    .AppendToolCall(
                        toolCall.CallIdDelta,
                        toolCall.NameDelta,
                        toolCall.ArgumentsDelta);
                break;
            case ChatReasoningStateReceived state:
                GetCandidate(state.CandidateIndex)
                    .GetItem(state.ItemIndex)
                    .SetState(state.State);
                break;
            case ChatCandidateCompleted completed:
                GetCandidate(completed.CandidateIndex).Complete(completed.FinishReason);
                break;
            case ChatUsageReceived usage:
                if (_usage is not null)
                {
                    throw new InvalidOperationException(
                        "The chat stream reported usage more than once.");
                }

                _usage = usage.Usage;
                break;
            default:
                throw new NotSupportedException(
                    $"Unsupported chat stream event {streamEvent.GetType().Name}.");
        }
    }

    public ChatResponse ToResponse()
    {
        if (_candidates.Count == 0)
        {
            throw new InvalidOperationException("The chat stream produced no candidates.");
        }

        var candidates = _candidates
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value.ToCandidate())
            .ToArray();
        return new ChatResponse(candidates, _usage);
    }

    private CandidateBuffer GetCandidate(int candidateIndex)
    {
        if (_candidates.TryGetValue(candidateIndex, out var candidate))
        {
            return candidate;
        }

        candidate = new CandidateBuffer();
        _candidates[candidateIndex] = candidate;
        return candidate;
    }

    private sealed class CandidateBuffer
    {
        private readonly Dictionary<int, ItemBuffer> _items = [];
        private FinishReason? _finishReason;

        public ItemBuffer GetItem(int itemIndex)
        {
            if (_items.TryGetValue(itemIndex, out var item))
            {
                return item;
            }

            item = new ItemBuffer();
            _items[itemIndex] = item;
            return item;
        }

        public void Complete(FinishReason finishReason)
        {
            if (_finishReason is not null)
            {
                throw new InvalidOperationException(
                    "The chat stream completed a candidate more than once.");
            }

            _finishReason = finishReason;
        }

        public ChatCandidate ToCandidate()
        {
            if (_finishReason is null)
            {
                throw new InvalidOperationException(
                    "The chat stream ended before a candidate completed.");
            }

            var items = _items
                .OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Value.TryToItem())
                .OfType<ChatItem>()
                .ToArray();
            return new ChatCandidate(items, _finishReason);
        }
    }

    private sealed class ItemBuffer
    {
        private readonly Dictionary<int, StringBuilder> _reasoningSegments = [];
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _callId = new();
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();
        private ItemKind _kind;
        private ChatRole? _role;
        private ReasoningTextKind? _reasoningKind;
        private OpaqueReasoningState? _state;

        public void AppendReasoning(int textSegmentIndex, ReasoningTextKind kind, string text)
        {
            SetKind(ItemKind.Reasoning);
            _reasoningKind ??= kind;
            if (_reasoningKind != kind)
            {
                throw new InvalidOperationException(
                    "A streamed reasoning item changed its text classification.");
            }

            if (!_reasoningSegments.TryGetValue(textSegmentIndex, out var segment))
            {
                segment = new StringBuilder();
                _reasoningSegments[textSegmentIndex] = segment;
            }

            segment.Append(text);
        }

        public void AppendText(ChatRole role, string text)
        {
            SetKind(ItemKind.Message);
            _role ??= role;
            if (_role != role)
            {
                throw new InvalidOperationException(
                    "A streamed message item changed its role.");
            }

            _text.Append(text);
        }

        public void AppendToolCall(string callIdDelta, string nameDelta, string argumentsDelta)
        {
            SetKind(ItemKind.ToolCall);
            _callId.Append(callIdDelta);
            _name.Append(nameDelta);
            _arguments.Append(argumentsDelta);
        }

        public void SetState(OpaqueReasoningState state)
        {
            SetKind(ItemKind.Reasoning);
            if (_state is not null)
            {
                throw new InvalidOperationException(
                    "A streamed reasoning item received opaque state more than once.");
            }

            _state = state;
        }

        public ChatItem? TryToItem() =>
            _kind switch
            {
                ItemKind.Reasoning => TryToReasoningItem(),
                ItemKind.Message => new ChatMessage(
                    _role ?? ChatRole.Assistant,
                    _text.ToString()),
                ItemKind.ToolCall => new ToolCall(
                    _callId.ToString(),
                    _name.ToString(),
                    _arguments.ToString()),
                _ => throw new InvalidOperationException(
                    "A streamed item received no content.")
            };

        private ChatReasoningItem? TryToReasoningItem()
        {
            var kind = _reasoningKind ?? ReasoningTextKind.Trace;
            var segments = _reasoningSegments
                .OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Value.ToString())
                .Where(static text => text.Length > 0)
                .Select(text => new ReasoningText(text, kind))
                .ToArray();
            if (segments.Length == 0 && _state is null)
            {
                return null;
            }

            return new ChatReasoningItem(new ReasoningContent(segments, _state));
        }

        private void SetKind(ItemKind kind)
        {
            if (_kind == ItemKind.None)
            {
                _kind = kind;
                return;
            }

            if (_kind != kind)
            {
                throw new InvalidOperationException(
                    "A streamed item mixed incompatible delta types.");
            }
        }
    }

    private enum ItemKind
    {
        None,
        Message,
        Reasoning,
        ToolCall
    }
}
