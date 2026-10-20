using System.Runtime.CompilerServices;

using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Tools;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class ChildVisionClient : IStreamingMultimodalChatClient
{
    public MultimodalChatCapabilities Capabilities { get; } = new(
        [new MultimodalContentCapability(ContentModality.Text),
            new MultimodalContentCapability(ContentModality.Image, [MediaSourceKind.Inline, MediaSourceKind.Uri])],
        [new MultimodalContentCapability(ContentModality.Text)], supportsTools: true);

    public List<MultimodalChatRequest> Requests { get; } = [];

    public Task<MultimodalChatResponse> CompleteAsync(MultimodalChatRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<MultimodalChatStreamEvent> StreamAsync(MultimodalChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.Items.OfType<Crystal.Multimodal.Tools.MultimodalToolResult>().Any())
        {
            yield return new MultimodalToolCallDelta(0, 0, "image", "view_image", """{"path":"sample.png"}""", ToolInputKind.Json);
            yield return new MultimodalChatUsageReceived(new TokenUsage(20, 3, 1));
            yield return new MultimodalChatCandidateCompleted(0, FinishReason.ToolCalls);
            yield break;
        }
        yield return new MultimodalMessageStarted(0, 0, MultimodalChatRole.Assistant);
        yield return new MultimodalMessageTextDelta(0, 0, 0, "Image inspected");
        yield return new MultimodalChatUsageReceived(new TokenUsage(10, 2, 1));
        yield return new MultimodalChatCandidateCompleted(0, FinishReason.Stop);
    }
}
