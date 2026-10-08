using System.Text;

using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Reasoning;

using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Prompts;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Asks a separate vision model to describe one image. The side request
/// carries no tools and is not part of the session transcript.
/// </summary>
public sealed class ImageDescriber
{
    private readonly Func<IStreamingMultimodalChatClient> _client;
    private readonly string _systemText;
    private readonly string _userTemplate;
    private readonly ReasoningOptions? _reasoning;
    private readonly PluginPlaceholderTable? _placeholders;

    public ImageDescriber(
        Func<IStreamingMultimodalChatClient> client,
        string systemText,
        ReasoningOptions? reasoning,
        string? userTemplate = null,
        PluginPlaceholderTable? placeholders = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemText);
        _client = client;
        _systemText = systemText.Trim();
        _userTemplate = string.IsNullOrWhiteSpace(userTemplate)
            ? ImageDescriptionPrompt.UserTemplate
            : userTemplate.Trim();
        _reasoning = reasoning;
        _placeholders = placeholders;
    }

    public async Task<string> DescribeAsync(
        ReadOnlyMemory<byte> data,
        string mimeType,
        string? question,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        cancellationToken.ThrowIfCancellationRequested();
        var client = _client();
        var response = await client.CompleteAsync(
            new MultimodalChatRequest(
                [
                    new MultimodalMessage(
                        MultimodalChatRole.System,
                        [new TextContent(_systemText)]),
                    new MultimodalMessage(
                        MultimodalChatRole.User,
                        [
                            new TextContent(ImageDescriptionPrompt.UserText(question, _userTemplate, _placeholders)),
                            new ImageContent(new ImageMedia(
                                new InlineMediaSource(data),
                                new MediaMimeType(mimeType)))
                        ])
                ],
                reasoning: _reasoning),
            cancellationToken);
        return ReadDescription(response);
    }

    private static string ReadDescription(MultimodalChatResponse response)
    {
        var builder = new StringBuilder();
        foreach (var item in response.Candidates[0].Items)
        {
            if (item is not MultimodalMessage message
                || message.Role != MultimodalChatRole.Assistant)
            {
                continue;
            }

            foreach (var content in message.Contents)
            {
                if (content is not TextContent { Text.Length: > 0 } text)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }
}
