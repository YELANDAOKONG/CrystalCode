using System.Text;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Media;
using Crystal.Tools;

using CrystalCode.Engine.Tools;

using ModelContextProtocol.Protocol;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Turns an MCP tool result into the plain text Crystal returns to the model.
/// </summary>
internal static class McpToolResultText
{
    public const string EmptyResult = "The MCP tool returned no content.";

    public static ToolOutput ToOutput(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = Format(result);
        var status = result.IsError == true ? ToolResultStatus.Failure : ToolResultStatus.Success;
        return new ToolOutput(ToolOutputText.Truncate(text), status);
    }

    public static MultimodalToolOutput ToMultimodalOutput(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var contents = new List<MultimodalContent>();
        var hasText = false;
        foreach (var block in result.Content ?? [])
        {
            switch (block)
            {
                case TextContentBlock text:
                    contents.Add(new TextContent(text.Text ?? string.Empty));
                    hasText = true;
                    break;
                case ImageContentBlock image:
                    if (TryImage(image, out var content))
                    {
                        contents.Add(content);
                    }
                    else
                    {
                        contents.Add(new TextContent($"[image content omitted: {MimeName(image.MimeType)}]"));
                    }
                    break;
                default:
                    contents.Add(new TextContent(Describe(block)));
                    break;
            }
        }

        if (!hasText && result.StructuredContent is { } structured)
        {
            contents.Add(new TextContent(structured.GetRawText()));
        }

        if (contents.Count == 0)
        {
            contents.Add(new TextContent(EmptyResult));
        }

        return new MultimodalToolOutput(
            contents,
            result.IsError == true ? MultimodalToolResultStatus.Failure : MultimodalToolResultStatus.Success);
    }

    private static bool TryImage(ImageContentBlock image, out ImageContent content)
    {
        content = null!;
        if (!IsSupportedImage(image.MimeType) || image.Data is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            content = new ImageContent(new ImageMedia(
                new InlineMediaSource(image.Data),
                new MediaMimeType(image.MimeType)));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSupportedImage(string? mimeType) =>
        mimeType is not null && (mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("image/webp", StringComparison.OrdinalIgnoreCase));

    private static string Format(CallToolResult result)
    {
        var parts = new List<string>();
        var hasText = false;
        foreach (var block in result.Content ?? [])
        {
            hasText |= block is TextContentBlock;
            parts.Add(Describe(block));
        }

        // A server should repeat structured content as text. When it does not,
        // the structured value is the only copy of the answer.
        if (!hasText && result.StructuredContent is { } structured)
        {
            parts.Add(structured.GetRawText());
        }

        var joined = string.Join(ToolOutputText.LineSeparator, parts.Where(part => part.Length > 0));
        return joined.Length == 0 ? EmptyResult : joined;
    }

    // Binary content is not sent to the model. Naming the kind lets it say
    // that something was left out instead of guessing.
    private static string Describe(ContentBlock block) =>
        block switch
        {
            TextContentBlock text => text.Text ?? string.Empty,
            ImageContentBlock image => $"[image content omitted: {MimeName(image.MimeType)}]",
            AudioContentBlock audio => $"[audio content omitted: {MimeName(audio.MimeType)}]",
            ResourceLinkBlock link => DescribeLink(link),
            EmbeddedResourceBlock embedded => DescribeResource(embedded.Resource),
            _ => $"[unsupported MCP content omitted: {block.Type}]"
        };

    private static string DescribeLink(ResourceLinkBlock link)
    {
        var builder = new StringBuilder("[resource link: ").Append(link.Uri);
        if (!string.IsNullOrWhiteSpace(link.Name))
        {
            builder.Append(" (").Append(link.Name).Append(')');
        }

        return builder.Append(']').ToString();
    }

    private static string DescribeResource(ResourceContents? resource) =>
        resource switch
        {
            TextResourceContents text => text.Text ?? string.Empty,
            BlobResourceContents blob =>
                $"[binary resource omitted: {blob.Uri} ({MimeName(blob.MimeType)})]",
            _ => "[empty resource]"
        };

    private static string MimeName(string? mimeType) =>
        string.IsNullOrWhiteSpace(mimeType) ? "unknown type" : mimeType;
}
