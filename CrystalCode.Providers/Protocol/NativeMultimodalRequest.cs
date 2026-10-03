using System.Text;

using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;

namespace CrystalCode.Providers.Protocol;

internal sealed class NativeMultimodalRequest
{
    private readonly Dictionary<string, ImageContent> _images = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ImageContent> Images => _images;

    public ChatRequest Convert(MultimodalChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = request.Items.Select(ConvertItem).ToArray();
        return new ChatRequest(items, request.Tools, request.Reasoning);
    }

    private ChatItem ConvertItem(MultimodalChatItem item) => item switch
    {
        MultimodalMessage message => new ChatMessage(
            new ChatRole(message.Role.Value),
            Flatten(message.Contents, message.Role == MultimodalChatRole.User)),
        MultimodalReasoningItem reasoning => new ChatReasoningItem(
            new ReasoningContent(
                reasoning.Content.Parts.Select(static part =>
                    new ReasoningText(
                        part.Content is TextContent text
                            ? text.Text
                            : throw new NotSupportedException("Image reasoning replay is unsupported."),
                        part.Kind == MultimodalReasoningKind.Trace
                            ? ReasoningTextKind.Trace
                            : ReasoningTextKind.Summary)),
                reasoning.Content.State)),
        MultimodalToolCall call when call.Contents.Count == 0 =>
            new ToolCall(call.CallId, call.Name, call.Arguments),
        MultimodalToolCall => throw new NotSupportedException(
            "Multimodal tool-call content is unsupported."),
        MultimodalToolResult result => new ToolResult(
            result.CallId,
            Flatten(result.Contents, allowImages: true),
            result.Status == MultimodalToolResultStatus.Success
                ? ToolResultStatus.Success
                : ToolResultStatus.Failure),
        _ => throw new NotSupportedException(
            $"Multimodal chat item {item.GetType().Name} is unsupported.")
    };

    private string Flatten(IReadOnlyList<MultimodalContent> contents, bool allowImages)
    {
        var text = new StringBuilder();
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent value:
                    text.Append(value.Text);
                    break;
                case ImageContent image when allowImages:
                    var token = $"\u001fcrystal-image-{Guid.NewGuid():N}\u001f";
                    _images.Add(token, image);
                    text.Append(token);
                    break;
                case ImageContent:
                    throw new NotSupportedException(
                        "Native provider images are supported only in user messages and tool results.");
                default:
                    throw new NotSupportedException(
                        $"Multimodal input {content.Modality.Value} is unsupported.");
            }
        }

        return text.ToString();
    }
}
