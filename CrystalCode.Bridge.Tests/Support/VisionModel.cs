using System.Runtime.CompilerServices;

using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Bridge.Tests.Support;

/// <summary>An image-capable model that answers every request with one line.</summary>
internal sealed class VisionModel : IStreamingMultimodalChatClient
{
    public MultimodalChatCapabilities Capabilities { get; } = new(
        [new MultimodalContentCapability(ContentModality.Text),
            new MultimodalContentCapability(ContentModality.Image, [MediaSourceKind.Inline, MediaSourceKind.Uri])],
        [new MultimodalContentCapability(ContentModality.Text)], supportsTools: true);

    public List<MultimodalChatRequest> Requests { get; } = [];

    public Task<MultimodalChatResponse> CompleteAsync(MultimodalChatRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<MultimodalChatStreamEvent> StreamAsync(
        MultimodalChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        await Task.Yield();
        yield return new MultimodalMessageStarted(0, 0, MultimodalChatRole.Assistant);
        yield return new MultimodalMessageTextDelta(0, 0, 0, "I see it");
        yield return new MultimodalChatCandidateCompleted(0, FinishReason.Stop);
    }
}
