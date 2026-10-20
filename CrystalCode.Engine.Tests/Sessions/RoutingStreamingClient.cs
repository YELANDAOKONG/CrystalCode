using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Crystal.Chat;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class RoutingStreamingClient(
    Func<ChatRequest, CancellationToken, Task<IReadOnlyList<ChatStreamEvent>>> route) : IStreamingChatClient
{
    public ConcurrentQueue<ChatRequest> Requests { get; } = new();

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        foreach (var item in await route(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use streaming requests in this test.");
}
