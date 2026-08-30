using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Prompts;

namespace CrystalHarness.Compaction;

/// <summary>
/// Pins recent turns and replaces older tool noise with one summary message.
/// </summary>
public sealed class ContextCompactor
{
    public const int RecentUserTurnCount = 2;
    public const string OmittedResultText = "Tool result omitted after compaction.";
    private const int MaximumExcerptCharacters = 8_000;

    private readonly IChatClient _client;

    public ContextCompactor(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<CompactionOutcome> CompactAsync(
        IReadOnlyList<ChatItem> transcript,
        string todos,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(todos);
        cancellationToken.ThrowIfCancellationRequested();
        if (transcript.Count == 0)
        {
            return new CompactionOutcome(transcript, false);
        }

        var recentStart = FindRecentStart(transcript);
        var excerpt = BuildExcerpt(transcript, recentStart);
        if (excerpt.Length == 0)
        {
            return DropOldestToolResults(transcript, recentStart);
        }

        try
        {
            var response = await _client.CompleteAsync(
                new ChatRequest(
                [
                    new ChatMessage(ChatRole.System, CompactionPrompt.SystemText),
                    new ChatMessage(ChatRole.User, CompactionPrompt.UserText(excerpt, todos))
                ]),
                cancellationToken);
            var summary = ReadAssistantText(response);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return DropOldestToolResults(transcript, recentStart);
            }

            return new CompactionOutcome(
                Rebuild(transcript, recentStart, summary.Trim(), todos),
                true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return DropOldestToolResults(transcript, recentStart);
        }
    }

    private static int FindRecentStart(IReadOnlyList<ChatItem> transcript)
    {
        var userIndexes = new List<int>();
        for (var i = 0; i < transcript.Count; i++)
        {
            if (IsUser(transcript[i]))
            {
                userIndexes.Add(i);
            }
        }

        if (userIndexes.Count == 0)
        {
            return transcript.Count;
        }

        var keepFrom = Math.Max(0, userIndexes.Count - RecentUserTurnCount);
        return userIndexes[keepFrom];
    }

    private static string BuildExcerpt(IReadOnlyList<ChatItem> transcript, int recentStart)
    {
        var parts = new List<string>();
        var length = 0;
        for (var i = 1; i < recentStart && length < MaximumExcerptCharacters; i++)
        {
            var piece = transcript[i] switch
            {
                ToolCall call => call.Name + " " + call.Arguments,
                ToolResult result => result.Text,
                ChatMessage message when IsEarlierContext(message) => message.Text,
                _ => string.Empty
            };
            if (piece.Length == 0)
            {
                continue;
            }

            parts.Add(piece);
            length += piece.Length;
        }

        var excerpt = string.Join("\n\n", parts);
        return excerpt.Length <= MaximumExcerptCharacters
            ? excerpt
            : excerpt[..MaximumExcerptCharacters];
    }

    private static IReadOnlyList<ChatItem> Rebuild(
        IReadOnlyList<ChatItem> transcript,
        int recentStart,
        string summary,
        string todos)
    {
        var kept = new List<ChatItem> { transcript[0] };
        kept.Add(new ChatMessage(ChatRole.System, FormatSummary(summary, todos)));
        for (var i = 1; i < recentStart; i++)
        {
            if (transcript[i] is ChatMessage message
                && !IsEarlierContext(message)
                && (message.Role == ChatRole.User || message.Role == ChatRole.Assistant))
            {
                kept.Add(message);
            }
        }

        for (var i = recentStart; i < transcript.Count; i++)
        {
            kept.Add(transcript[i]);
        }

        return kept;
    }

    private static CompactionOutcome DropOldestToolResults(
        IReadOnlyList<ChatItem> transcript,
        int recentStart)
    {
        var next = new List<ChatItem>(transcript.Count);
        var dropped = false;
        for (var i = 0; i < transcript.Count; i++)
        {
            if (i > 0
                && i < recentStart
                && transcript[i] is ToolResult result
                && result.Text != OmittedResultText)
            {
                next.Add(new ToolResult(result.CallId, OmittedResultText, result.Status));
                dropped = true;
                continue;
            }

            next.Add(transcript[i]);
        }

        return new CompactionOutcome(next, dropped);
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

    private static string ReadAssistantText(ChatResponse response)
    {
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

    private static bool IsUser(ChatItem item) =>
        item is ChatMessage { Role.Value: "user" };

    private static bool IsEarlierContext(ChatMessage message) =>
        message.Role == ChatRole.System
        && message.Text.StartsWith(CompactionPrompt.Marker, StringComparison.Ordinal);
}
