using System.Runtime.CompilerServices;

using Crystal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Bridge.Tests.Support;

/// <summary>
/// A model that replays scripted rounds and then holds the stream open until
/// cancelled, registered for the Ollama protocol so no key is needed. Every
/// request is recorded, so a test can read what the model was sent.
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

    /// <summary>Completes each time the model holds a stream open.</summary>
    public TaskCompletionSource Held { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<ChatRequest> Requests { get; } = [];

    /// <summary>The image-capable client for models that declare image input.</summary>
    public VisionModel? Vision { get; init; }

    public PluginContribution Contribute() => new(clients: [this]);

    public bool CanCreate(ProviderProtocol protocol) => protocol == ProviderProtocol.Ollama;

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey) => this;

    public Crystal.Multimodal.Chat.IStreamingMultimodalChatClient? CreateMultimodal(HarnessSettings settings, string apiKey) =>
        settings.ActiveModel.ImageInput ? Vision : null;

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        lock (_rounds)
        {
            Requests.Add(request);
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

    /// <summary>The text of the last user message the model was sent.</summary>
    public string LastUserText()
    {
        lock (_rounds)
        {
            var message = Requests[^1].Items.OfType<ChatMessage>().Last(static item => item.Role == ChatRole.User);
            return message.Text;
        }
    }

    public static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, Crystal.FinishReason.Stop)
    ];

    public static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        new ChatToolCallDelta(0, 0, callId, name, arguments, Crystal.Tools.ToolInputKind.Json),
        new ChatCandidateCompleted(0, Crystal.FinishReason.ToolCalls)
    ];

    private async IAsyncEnumerable<ChatStreamEvent> HoldAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatTextDelta(0, 0, ChatRole.Assistant, "working");
        TaskCompletionSource held;
        lock (_rounds)
        {
            held = Held;
            Held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        held.TrySetResult();
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
