using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Tools;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class MultimodalStreamingTurnTests
{
    [Fact]
    public async Task RunAsync_ReplaysToolImageIntoNextModelRound()
    {
        var client = new ScriptedClient();
        var images = new Dictionary<int, ImageAttachment>();
        var turn = new MultimodalStreamingTurn(
            client,
            new ImageToolExecutor(),
            new TurnLimits(4, 4, TimeSpan.FromSeconds(5)),
            images);

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "render")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        var toolResult = Assert.Single(result.Transcript.OfType<ToolResult>());
        Assert.Contains("[Image #1]", toolResult.Text, StringComparison.Ordinal);
        Assert.Single(images);
        Assert.Equal(2, client.Requests.Count);
        var replay = Assert.IsType<MultimodalToolResult>(
            client.Requests[1].Items.OfType<MultimodalToolResult>().Single());
        Assert.Contains(replay.Contents, content => content is ImageContent);
    }

    private sealed class ScriptedClient : IStreamingMultimodalChatClient
    {
        public MultimodalChatCapabilities Capabilities { get; } = new(
            [
                new MultimodalContentCapability(ContentModality.Text),
                new MultimodalContentCapability(
                    ContentModality.Image,
                    [MediaSourceKind.Inline, MediaSourceKind.Uri])
            ],
            [new MultimodalContentCapability(ContentModality.Text)],
            supportsTools: true);

        public List<MultimodalChatRequest> Requests { get; } = [];

        public Task<MultimodalChatResponse> CompleteAsync(
            MultimodalChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<MultimodalChatStreamEvent> StreamAsync(
            MultimodalChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Requests.Count == 1)
            {
                yield return new MultimodalToolCallDelta(
                    0,
                    0,
                    "call_1",
                    "render",
                    "{}");
                yield return new MultimodalChatCandidateCompleted(
                    0,
                    FinishReason.ToolCalls);
                yield break;
            }

            yield return new MultimodalMessageStarted(
                0,
                0,
                MultimodalChatRole.Assistant);
            yield return new MultimodalMessageTextDelta(0, 0, 0, "done");
            yield return new MultimodalChatCandidateCompleted(0, FinishReason.Stop);
        }
    }

    private sealed class ImageToolExecutor : IMultimodalToolExecutor
    {
        public IReadOnlyList<ToolDefinition> Definitions { get; } =
        [
            new ToolDefinition(
                "render",
                JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
                "Render an image")
        ];

        public Task<IReadOnlyList<MultimodalToolResult>> ExecuteAsync(
            IEnumerable<MultimodalToolCall> calls,
            CancellationToken cancellationToken = default)
        {
            var call = Assert.Single(calls);
            IReadOnlyList<MultimodalToolResult> results =
            [
                new MultimodalToolResult(
                    call.CallId,
                    [
                        new TextContent("rendered"),
                        new ImageContent(new ImageMedia(
                            new InlineMediaSource(new byte[] { 1, 2, 3 }),
                            new MediaMimeType("image/png")))
                    ])
            ];
            return Task.FromResult(results);
        }
    }
}
