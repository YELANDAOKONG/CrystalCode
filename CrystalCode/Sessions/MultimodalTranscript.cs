using System.Text.RegularExpressions;

using Crystal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;

namespace CrystalCode.Sessions;

/// <summary>Projects the text transcript and referenced images onto Crystal contracts.</summary>
public static partial class MultimodalTranscript
{
    public static IReadOnlyList<MultimodalChatItem> Convert(
        IReadOnlyList<ChatItem> items,
        IDictionary<int, ImageAttachment> images)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(images);
        return [.. items.Select(item => ConvertItem(item, images))];
    }

    private static MultimodalChatItem ConvertItem(
        ChatItem item,
        IDictionary<int, ImageAttachment> images) => item switch
    {
        ChatMessage message => new MultimodalMessage(
            new MultimodalChatRole(message.Role.Value),
            ConvertMessage(message.Text, images)),
        ChatReasoningItem reasoning => new MultimodalReasoningItem(
            new MultimodalReasoningContent(
                reasoning.Content.TextSegments.Select(static text =>
                    new MultimodalReasoningPart(
                        new TextContent(text.Text),
                        text.Kind == ReasoningTextKind.Trace
                            ? MultimodalReasoningKind.Trace
                            : MultimodalReasoningKind.Summary)),
                reasoning.Content.State)),
        ToolCall call => new MultimodalToolCall(
            call.CallId,
            call.Name,
            call.Arguments),
        ToolResult result => new MultimodalToolResult(
            result.CallId,
            ConvertMessage(result.Text, images),
            result.Status == ToolResultStatus.Success
                ? MultimodalToolResultStatus.Success
                : MultimodalToolResultStatus.Failure),
        _ => throw new NotSupportedException(
            $"Multimodal transcript does not support {item.GetType().Name}.")
    };

    private static IReadOnlyList<MultimodalContent> ConvertMessage(
        string text,
        IDictionary<int, ImageAttachment> images)
    {
        var contents = new List<MultimodalContent>();
        var cursor = 0;
        foreach (Match match in ImageMarker().Matches(text))
        {
            if (!int.TryParse(match.Groups[1].Value, out var number)
                || !images.TryGetValue(number, out var image))
            {
                continue;
            }

            if (match.Index > cursor)
            {
                contents.Add(new TextContent(text[cursor..match.Index]));
            }

            contents.Add(new ImageContent(new ImageMedia(
                image.Uri is null
                    ? new InlineMediaSource(image.Data!.Value)
                    : new UriMediaSource(image.Uri),
                new MediaMimeType(image.MimeType))));
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length || contents.Count == 0)
        {
            contents.Add(new TextContent(text[cursor..]));
        }

        return contents;
    }

    [GeneratedRegex(@"\[Image #(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ImageMarker();
}
