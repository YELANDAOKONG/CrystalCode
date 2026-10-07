using Crystal.Chat;

using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;
using CrystalCode.Plugins.Hooks;

namespace CrystalCode.Engine.Compaction;

/// <summary>
/// Summarizes older turns into one structured message and keeps a recent tail.
/// </summary>
public sealed class ContextCompactor
{
    public const string OmittedResultText = "Tool result omitted after compaction.";

    private readonly IChatClient _client;
    private readonly Func<string> _systemText;
    private readonly SessionRetryOptions _retry;
    private readonly Action<SessionRetryAttempt>? _onRetry;
    private readonly Func<PluginCompactionPhase, string, string?>? _finish;
    private readonly Func<IReadOnlyList<ChatItem>, CancellationToken, Task<IReadOnlyList<ChatItem>>>?
        _prepareHead;
    private readonly Func<ChatResponse, CancellationToken, Task>? _reportResponse;
    private readonly Func<string, string, string?, string>? _userText;

    public ContextCompactor(
        IChatClient client,
        SessionRetryOptions? retry = null,
        Action<SessionRetryAttempt>? onRetry = null,
        Func<string>? systemText = null,
        Func<PluginCompactionPhase, string, string?>? finish = null,
        Func<IReadOnlyList<ChatItem>, CancellationToken, Task<IReadOnlyList<ChatItem>>>? prepareHead = null,
        Func<ChatResponse, CancellationToken, Task>? reportResponse = null,
        Func<string, string, string?, string>? userText = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _retry = retry ?? SessionRetryOptions.Default;
        _onRetry = onRetry;
        _finish = finish;
        _prepareHead = prepareHead;
        _reportResponse = reportResponse;
        _userText = userText;
        _systemText = systemText
            ?? (() => CompactionPrompt.ComposeSystem(
                PromptContext.InstructionsOnly(string.Empty).WithMode("compaction")));
    }

    /// <summary>
    /// Summarizes older turns. When <paramref name="force"/> is set, history
    /// that still fits in the retained tail is summarized too, and only the
    /// latest user turn stays verbatim.
    /// </summary>
    public async Task<CompactionOutcome> CompactAsync(
        IReadOnlyList<ChatItem> transcript,
        string todos,
        CompactionLimits limits,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        if (transcript.Count == 0)
        {
            return new CompactionOutcome(transcript, CompactionKind.Unchanged);
        }

        var pruned = ToolResultPruner.Prune(transcript, OmittedResultText);
        var prunedChanged = WasRewritten(transcript, pruned);
        var split = CompactionSelection.Choose(pruned, limits.ResolveTailBudget());
        if (split.Head.Count == 0 && force)
        {
            split = CompactionSelection.ChooseLatestTurn(pruned);
        }

        if (split.Head.Count == 0)
        {
            return prunedChanged
                ? new CompactionOutcome(pruned, CompactionKind.Applied)
                : new CompactionOutcome(transcript, CompactionKind.Unchanged);
        }

        var head = split.Head;
        if (_prepareHead is not null && head.Count > 0)
        {
            head = await _prepareHead(head, cancellationToken);
            ArgumentNullException.ThrowIfNull(head);
        }

        var conversation = CompactionText.Conversation(head);
        if (conversation.Length == 0)
        {
            return FinishWithoutSummary(pruned, prunedChanged);
        }

        var prompt = _userText is null
            ? CompactionPrompt.UserText(conversation, todos, split.PreviousSummary)
            : _userText(conversation, todos, split.PreviousSummary);
        prompt = Finish(PluginCompactionPhase.Prompt, prompt);
        var systemText = _systemText();
        var promptTokens = TokenEstimator.Text(systemText) + TokenEstimator.Text(prompt);
        if (promptTokens > limits.SummaryPromptBudget())
        {
            return FinishWithoutSummary(pruned, prunedChanged);
        }

        ChatResponse response;
        try
        {
            response = await SessionRetry.RunAsync(
                token => _client.CompleteAsync(
                    new ChatRequest(
                    [
                        new ChatMessage(ChatRole.System, systemText),
                        new ChatMessage(ChatRole.User, prompt)
                    ]),
                    token),
                _retry,
                _onRetry,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return FinishWithoutSummary(pruned, prunedChanged);
        }

        if (_reportResponse is not null && response.Candidates.Count > 0)
        {
            await _reportResponse(response, cancellationToken);
        }

        var summary = ReadAssistantText(response);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return FinishWithoutSummary(pruned, prunedChanged);
        }

        summary = Finish(PluginCompactionPhase.Summary, summary.Trim());
        var stored = FormatSummary(summary, todos);
        return new CompactionOutcome(
            Rebuild(pruned, split, stored),
            CompactionKind.Applied,
            stored);
    }

    private static CompactionOutcome FinishWithoutSummary(
        IReadOnlyList<ChatItem> pruned,
        bool prunedChanged) =>
        prunedChanged
            ? new CompactionOutcome(pruned, CompactionKind.Applied)
            : new CompactionOutcome(pruned, CompactionKind.Exhausted);

    private static IReadOnlyList<ChatItem> Rebuild(
        IReadOnlyList<ChatItem> pruned,
        CompactionSplit split,
        string storedSummary)
    {
        var kept = new List<ChatItem>();
        if (pruned.Count > 0
            && pruned[0] is ChatMessage system
            && system.Role == ChatRole.System
            && !CompactionSelection.IsSummary(system))
        {
            kept.Add(system);
        }

        kept.Add(new ChatMessage(ChatRole.System, storedSummary));
        kept.AddRange(split.Tail);
        return kept;
    }

    private static bool WasRewritten(IReadOnlyList<ChatItem> original, IReadOnlyList<ChatItem> next)
    {
        if (original.Count != next.Count)
        {
            return true;
        }

        for (var i = 0; i < original.Count; i++)
        {
            if (!ReferenceEquals(original[i], next[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatSummary(string summary, string todos)
    {
        var text = CompactionPrompt.Marker + "\n" + summary;
        if (todos.Trim().Length > 0)
        {
            text += "\n\n## Open todos\n" + todos.Trim();
        }

        return text;
    }

    private string Finish(PluginCompactionPhase phase, string text)
    {
        if (_finish is null)
        {
            return text;
        }

        return _finish(phase, text) ?? text;
    }

    private static string ReadAssistantText(ChatResponse response)
    {
        if (response.Candidates.Count == 0)
        {
            return string.Empty;
        }

        foreach (var item in response.Candidates[0].Items)
        {
            if (item is ChatMessage message
                && message.Role == ChatRole.Assistant
                && !string.IsNullOrWhiteSpace(message.Text))
            {
                return message.Text;
            }
        }

        return string.Empty;
    }
}
