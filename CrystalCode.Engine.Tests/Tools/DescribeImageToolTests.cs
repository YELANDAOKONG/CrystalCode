using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class DescribeImageToolTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    [Fact]
    public async Task InvokeAsync_SendsTheDescriptionPromptQuestionAndImage()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "shot.png");
        File.WriteAllBytes(path, Png);
        var client = new ScriptedImageClient("a red square");
        var reasoning = new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.Low);
        var system = ImageDescriptionPrompt.ComposeSystem(
            PromptContext.Create(root.Path, "openai", "gpt-5.6-sol", "image", string.Empty, string.Empty));
        var tool = new DescribeImageTool(
            new Workspace(root.Path),
            new ImageDescriber(() => client, system, reasoning));

        var output = await tool.InvokeAsync(
            new ToolCall("1", DescribeImageTool.ToolName, """{"path":"shot.png","question":"what color"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("a red square", output.Text);
        var request = Assert.IsType<MultimodalChatRequest>(client.Request);
        Assert.Empty(request.Tools);
        Assert.Equal(reasoning, request.Reasoning);
        var systemMessage = Assert.IsType<MultimodalMessage>(request.Items[0]);
        Assert.Equal(MultimodalChatRole.System, systemMessage.Role);
        Assert.Contains(
            "cannot see the image",
            Assert.IsType<TextContent>(systemMessage.Contents[0]).Text,
            StringComparison.Ordinal);
        var user = Assert.IsType<MultimodalMessage>(request.Items[1]);
        Assert.Equal("Question: what color", Assert.IsType<TextContent>(user.Contents[0]).Text);
        var image = Assert.IsType<ImageContent>(user.Contents[1]);
        Assert.Equal("image/png", image.Image.MimeType.Value);
        var source = Assert.IsType<InlineMediaSource>(image.Image.Source);
        Assert.True(source.Data.Span.SequenceEqual(Png));
    }

    [Fact]
    public async Task InvokeAsync_FailsWhenTheImageModelReturnsNoText()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(root.Path, "shot.png"), Png);
        var tool = new DescribeImageTool(
            new Workspace(root.Path),
            new ImageDescriber(
                () => new ScriptedImageClient(string.Empty),
                ImageDescriptionPrompt.DescribeRequest,
                reasoning: null));

        var output = await tool.InvokeAsync(
            new ToolCall("1", DescribeImageTool.ToolName, """{"path":"shot.png"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("The image model returned no description.", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ReportsAProviderFailureWithoutThrowing()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(root.Path, "shot.png"), Png);
        var tool = new DescribeImageTool(
            new Workspace(root.Path),
            new ImageDescriber(
                () => throw new InvalidOperationException("missing credential"),
                ImageDescriptionPrompt.DescribeRequest,
                reasoning: null));

        var output = await tool.InvokeAsync(
            new ToolCall("1", DescribeImageTool.ToolName, """{"path":"shot.png"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("The image model failed: missing credential", output.Text);
    }

    private sealed class ScriptedImageClient : IStreamingMultimodalChatClient
    {
        private readonly string _reply;

        public ScriptedImageClient(string reply)
        {
            _reply = reply;
        }

        public MultimodalChatRequest? Request { get; private set; }

        public MultimodalChatCapabilities Capabilities { get; } = new(
            [new MultimodalContentCapability(ContentModality.Text)],
            [new MultimodalContentCapability(ContentModality.Text)]);

        public Task<MultimodalChatResponse> CompleteAsync(
            MultimodalChatRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new MultimodalChatResponse(
            [
                new MultimodalChatCandidate(
                    [
                        new MultimodalMessage(
                            MultimodalChatRole.Assistant,
                            [new TextContent(_reply)])
                    ],
                    FinishReason.Stop)
            ]));
        }

        public IAsyncEnumerable<MultimodalChatStreamEvent> StreamAsync(
            MultimodalChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
