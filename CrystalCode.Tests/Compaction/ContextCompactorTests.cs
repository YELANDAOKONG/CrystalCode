using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Compaction;
using CrystalCode.Prompts;
using CrystalCode.Tests.Approvals;

using Xunit;

namespace CrystalCode.Tests.Compaction;

public sealed class ContextCompactorTests
{
    [Fact]
    public async Task CompactAsync_ReplacesOlderTurnsWithStructuredSummary()
    {
        var client = new FixedChatClient("## Objective\n- Read then write App.cs.");
        var compactor = new ContextCompactor(client);
        var transcript = LongTranscript();

        var outcome = await compactor.CompactAsync(transcript, "No todos.", TightLimits);

        Assert.Equal(CompactionKind.Applied, outcome.Kind);
        Assert.DoesNotContain(
            outcome.Transcript,
            item => item is ChatMessage { Role.Value: "user", Text: "first" });
        Assert.DoesNotContain(
            outcome.Transcript,
            item => item is ToolResult { Text: "file contents" });
        Assert.DoesNotContain(
            outcome.Transcript,
            item => item is ToolResult { Text: "wrote" });
        Assert.Contains(
            outcome.Transcript,
            item => item is ChatMessage message
                && message.Text.Contains("Read then write App.cs.", StringComparison.Ordinal)
                && message.Text.StartsWith(CompactionPrompt.Marker, StringComparison.Ordinal));
        Assert.Contains(
            outcome.Transcript,
            item => item is ChatMessage { Role.Value: "user", Text: "third" });
        Assert.NotNull(client.LastRequest);
        var prompt = string.Join('\n', client.LastRequest.Items.OfType<ChatMessage>().Select(message => message.Text));
        Assert.Contains("[User]: first", prompt, StringComparison.Ordinal);
        Assert.Contains("## Objective", prompt, StringComparison.Ordinal);
        Assert.Contains(client.LastRequest.Items.OfType<ChatMessage>(), message => message.Text == CompactionPrompt.SystemText);
    }

    [Fact]
    public async Task CompactAsync_FoldsPreviousSummaryIntoTheNextPrompt()
    {
        var client = new FixedChatClient("## Objective\n- Continue the edit.");
        var compactor = new ContextCompactor(client);
        var transcript = new List<ChatItem>
        {
            new ChatMessage(ChatRole.System, "work"),
            new ChatMessage(ChatRole.System, CompactionPrompt.Marker + "\nOld summary body."),
            new ChatMessage(ChatRole.User, "first"),
            new ChatMessage(ChatRole.Assistant, "ok"),
            new ChatMessage(ChatRole.User, "second"),
            new ChatMessage(ChatRole.Assistant, "done")
        };

        var outcome = await compactor.CompactAsync(
            transcript,
            "No todos.",
            new CompactionLimits(100_000, tailBudget: 1));

        Assert.Equal(CompactionKind.Applied, outcome.Kind);
        Assert.NotNull(client.LastRequest);
        var prompt = string.Join('\n', client.LastRequest.Items.OfType<ChatMessage>().Select(message => message.Text));
        Assert.Contains("<prior-summary>", prompt, StringComparison.Ordinal);
        Assert.Contains("Old summary body.", prompt, StringComparison.Ordinal);
        Assert.Equal(
            1,
            outcome.Transcript.Count(item => item is ChatMessage message && CompactionSelection.IsSummary(message)));
    }

    [Fact]
    public async Task CompactAsync_IsUnchangedWhenEverythingFitsInTheTail()
    {
        var client = new FixedChatClient("should not run");
        var compactor = new ContextCompactor(client);
        var transcript = LongTranscript();

        var outcome = await compactor.CompactAsync(
            transcript,
            "No todos.",
            new CompactionLimits(100_000, tailBudget: 10_000));

        Assert.Equal(CompactionKind.Unchanged, outcome.Kind);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public async Task CompactAsync_IsExhaustedWhenSummaryFailsAndNothingCanBePruned()
    {
        var compactor = new ContextCompactor(new EmptyChatClient());
        var transcript = LongTranscript();

        var outcome = await compactor.CompactAsync(transcript, "No todos.", TightLimits);

        Assert.Equal(CompactionKind.Exhausted, outcome.Kind);
        Assert.Equal(3, outcome.Transcript.OfType<ChatMessage>().Count(IsUser));
    }

    private static CompactionLimits TightLimits { get; } = new(100_000, tailBudget: 8);

    private static List<ChatItem> LongTranscript() =>
    [
        new ChatMessage(ChatRole.System, "work"),
        new ChatMessage(ChatRole.User, "first"),
        new ChatMessage(ChatRole.Assistant, "ok"),
        new ToolCall("1", "read", """{"path":"a.txt"}"""),
        new ToolResult("1", "file contents"),
        new ChatMessage(ChatRole.User, "second"),
        new ToolCall("2", "write", """{"path":"App.cs","contents":"x"}"""),
        new ToolResult("2", "wrote"),
        new ChatMessage(ChatRole.User, "third"),
        new ChatMessage(ChatRole.Assistant, "done")
    ];

    private static bool IsUser(ChatMessage message) => message.Role.Value == "user";

    private sealed class EmptyChatClient : IChatClient
    {
        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ChatResponse(
                [
                    new ChatCandidate(
                        [new ChatMessage(ChatRole.Assistant, " ")],
                        FinishReason.Stop)
                ]));
    }
}
