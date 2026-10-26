using System.Runtime.CompilerServices;

using Crystal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Server.Tests.Support;

/// <summary>
/// A model that replays scripted rounds and then holds the stream open until
/// cancelled, registered for the Ollama protocol so no key is needed.
/// </summary>
internal sealed class ScriptedModel : IStreamingChatClient, IPlugin, IChatClientFactory
{
    private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;
    private readonly bool _holdWhenEmpty;

    public ScriptedModel(bool holdWhenEmpty, params IReadOnlyList<ChatStreamEvent>[] rounds)
    {
        _holdWhenEmpty = holdWhenEmpty;
        _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
    }

    public string Name => "scripted";

    /// <summary>Hold without streaming any text first, so the turn shows no output.</summary>
    public bool Silent { get; init; }

    public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PluginContribution Contribute() => new(clients: [this]);

    public bool CanCreate(ProviderProtocol protocol) => protocol == ProviderProtocol.Ollama;

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey) => this;

    /// <summary>The image-capable client for models that declare image input.</summary>
    public VisionModel? Vision { get; init; }

    public Crystal.Multimodal.Chat.IStreamingMultimodalChatClient? CreateMultimodal(HarnessSettings settings, string apiKey) =>
        settings.ActiveModel.ImageInput ? Vision : null;

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        lock (_rounds)
        {
            if (_rounds.Count > 0)
            {
                return EnumerateAsync(_rounds.Dequeue(), cancellationToken);
            }
        }

        return _holdWhenEmpty
            ? HoldAsync(cancellationToken)
            : EnumerateAsync([new ChatTextDelta(0, 0, ChatRole.Assistant, "ok"), new ChatCandidateCompleted(0, Crystal.FinishReason.Stop)], cancellationToken);
    }

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The session streams.");

    private async IAsyncEnumerable<ChatStreamEvent> HoldAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Silent)
        {
            yield return new ChatTextDelta(0, 0, ChatRole.Assistant, "working");
        }

        Held.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
        IReadOnlyList<ChatStreamEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var value in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
            await Task.Yield();
        }
    }
}
