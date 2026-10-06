using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Plugins.Hooks;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Maps host tool results to and from the plugin hook contract.</summary>
internal static class PluginToolResults
{
    public static PluginToolResult From(ToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PluginToolResult(result.Text, result.Status == ToolResultStatus.Success);
    }

    public static ToolResult ToText(string callId, PluginToolResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(result);
        return new ToolResult(
            callId,
            result.Text,
            result.Success ? ToolResultStatus.Success : ToolResultStatus.Failure);
    }

    public static PluginToolResult From(MultimodalToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = new List<string>();
        var images = new List<PluginImage>();
        foreach (var content in result.Contents)
        {
            if (content is TextContent textContent)
            {
                text.Add(textContent.Text);
                continue;
            }

            if (content is ImageContent image
                && image.Image.Source is InlineMediaSource inline)
            {
                images.Add(new PluginImage(image.Image.MimeType.Value, inline.Data));
            }
        }

        return new PluginToolResult(
            string.Join("\n", text),
            result.Status == MultimodalToolResultStatus.Success,
            images);
    }

    public static MultimodalToolResult ToMultimodal(
        string callId,
        PluginToolResult result,
        IList<string> imageNotes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(imageNotes);
        var contents = new List<MultimodalContent> { new TextContent(result.Text) };
        foreach (var image in result.Images)
        {
            if (!TryImage(image, out var content, out var error))
            {
                imageNotes.Add(error);
                continue;
            }

            contents.Add(content!);
        }

        return new MultimodalToolResult(
            callId,
            contents,
            result.Success ? MultimodalToolResultStatus.Success : MultimodalToolResultStatus.Failure);
    }

    private static bool TryImage(PluginImage image, out ImageContent? content, out string error)
    {
        content = null;
        if (!IsSupported(image.MediaType))
        {
            error = $"Plugin image '{image.MediaType}' was ignored.";
            return false;
        }

        if (image.Data.IsEmpty)
        {
            error = "An empty plugin image was ignored.";
            return false;
        }

        try
        {
            content = new ImageContent(
                new ImageMedia(new InlineMediaSource(image.Data), new MediaMimeType(image.MediaType)));
            error = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsSupported(string mediaType) =>
        mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);
}
