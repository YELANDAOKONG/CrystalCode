using System.Text.Json;
using System.Text.Json.Nodes;

using Crystal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;

namespace CrystalCode.Providers.Compatible;

internal static class CompatibleMultimodalRequestWriter
{
    public static byte[] Write(
        CompatibleProfile profile,
        CompatibleOptions options,
        MultimodalChatRequest request,
        bool stream)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        var replacements = new Dictionary<string, ImageContent>(StringComparer.Ordinal);
        var items = request.Items.Select(item => ConvertItem(item, replacements)).ToArray();
        var body = CompatibleChatRequestWriter.Write(
            profile,
            options,
            new ChatRequest(items, request.Tools, request.Reasoning),
            stream);
        if (replacements.Count == 0)
        {
            return body;
        }

        var root = JsonNode.Parse(body)?.AsObject()
            ?? throw new JsonException("Compatible request body was not a JSON object.");
        var messages = root["messages"]?.AsArray()
            ?? throw new JsonException("Compatible request body was missing messages.");
        foreach (var messageNode in messages)
        {
            var message = messageNode?.AsObject();
            if (message?["content"] is not JsonValue contentValue
                || !contentValue.TryGetValue<string>(out var text))
            {
                continue;
            }

            var parts = Expand(text, replacements);
            if (parts is not null)
            {
                message["content"] = parts;
            }
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static ChatItem ConvertItem(
        MultimodalChatItem item,
        IDictionary<string, ImageContent> replacements) => item switch
    {
        MultimodalMessage message => new ChatMessage(
            new ChatRole(message.Role.Value),
            FlattenContents(message.Role, message.Contents, replacements)),
        MultimodalReasoningItem reasoning => new ChatReasoningItem(
            new ReasoningContent(
                reasoning.Content.Parts.Select(ConvertReasoningPart),
                reasoning.Content.State)),
        MultimodalToolCall call when call.Contents.Count == 0 => new ToolCall(
            call.CallId,
            call.Name,
            call.Arguments),
        MultimodalToolCall => throw new NotSupportedException(
            "Compatible Chat Completions does not support multimodal tool-call content."),
        MultimodalToolResult result => new ToolResult(
            result.CallId,
            FlattenContents(null, result.Contents, replacements),
            result.Status == MultimodalToolResultStatus.Success
                ? ToolResultStatus.Success
                : ToolResultStatus.Failure),
        _ => throw new NotSupportedException(
            $"Compatible Chat Completions does not support {item.GetType().Name}.")
    };

    private static ReasoningText ConvertReasoningPart(
        MultimodalReasoningPart part)
    {
        if (part.Content is not TextContent text)
        {
            throw new NotSupportedException(
                $"Compatible Chat Completions does not support {part.Content.Modality.Value} reasoning output replay.");
        }

        return new ReasoningText(
            text.Text,
            part.Kind == MultimodalReasoningKind.Trace
                ? ReasoningTextKind.Trace
                : ReasoningTextKind.Summary);
    }

    private static string FlattenContents(
        MultimodalChatRole? role,
        IReadOnlyList<MultimodalContent> contents,
        IDictionary<string, ImageContent> replacements)
    {
        var text = new System.Text.StringBuilder();
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent value:
                    text.Append(value.Text);
                    break;
                case ImageContent image when role is null
                    || role == MultimodalChatRole.User:
                    var token = $"\u001fcrystal-image-{Guid.NewGuid():N}\u001f";
                    replacements.Add(token, image);
                    text.Append(token);
                    break;
                case ImageContent:
                    throw new NotSupportedException(
                        "Compatible Chat Completions accepts images only in user and tool messages.");
                default:
                    throw new NotSupportedException(
                        $"Compatible Chat Completions does not support {content.Modality.Value} input.");
            }
        }

        return text.ToString();
    }

    private static JsonArray? Expand(
        string text,
        IReadOnlyDictionary<string, ImageContent> replacements)
    {
        var matches = replacements
            .Where(pair => text.Contains(pair.Key, StringComparison.Ordinal))
            .OrderBy(pair => text.IndexOf(pair.Key, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var parts = new JsonArray();
        var cursor = 0;
        foreach (var (token, image) in matches)
        {
            var index = text.IndexOf(token, cursor, StringComparison.Ordinal);
            if (index > cursor)
            {
                parts.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text[cursor..index]
                });
            }

            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = ImageUrl(image.Image)
                }
            });
            cursor = index + token.Length;
        }

        if (cursor < text.Length)
        {
            parts.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text[cursor..]
            });
        }

        return parts;
    }

    private static string ImageUrl(ImageMedia image) => image.Source switch
    {
        InlineMediaSource inline =>
            $"data:{image.MimeType.Value};base64,{Convert.ToBase64String(inline.Data.Span)}",
        UriMediaSource uri when uri.Uri.Scheme is "http" or "https"
            && uri.Uri.AbsoluteUri.Length <= 8192 => uri.Uri.AbsoluteUri,
        UriMediaSource => throw new NotSupportedException(
            "Compatible Chat Completions image URIs must be HTTP(S) and at most 8192 characters."),
        _ => throw new NotSupportedException(
            $"Compatible Chat Completions does not support image source {image.Source.Kind.Value}.")
    };
}
