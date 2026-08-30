using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Compaction;
using CrystalHarness.Prompts;
using CrystalHarness.Tests.Approvals;

using Xunit;

namespace CrystalHarness.Tests.Compaction;

public sealed class ContextCompactorTests
{
    [Fact]
    public async Task CompactAsync_ReplacesOlderToolNoiseWithSummary()
    {
        var client = new FixedChatClient("Read a.txt then wrote App.cs.");
        var compactor = new ContextCompactor(client);
        var transcript = LongTranscript();

        var outcome = await compactor.CompactAsync(transcript, "No todos.");

        Assert.True(outcome.Compacted);
        Assert.DoesNotContain(
            outcome.Transcript,
            item => item is ToolResult { Text: "file contents" });
        Assert.Contains(
            outcome.Transcript,
            item => item is ToolResult { Text: "wrote" });
        Assert.Contains(
            outcome.Transcript,
            item => item is ChatMessage message
                && message.Text.Contains("Read a.txt then wrote App.cs.", StringComparison.Ordinal)
                && message.Text.StartsWith(CompactionPrompt.Marker, StringComparison.Ordinal));
        Assert.Contains(
            outcome.Transcript,
            item => item is ChatMessage { Role.Value: "user", Text: "third" });
        Assert.NotNull(client.LastRequest);
        Assert.Contains(
            client.LastRequest.Items.OfType<ChatMessage>(),
            message => message.Role == ChatRole.System
                && message.Text == CompactionPrompt.SystemText);
    }

    [Fact]
    public async Task CompactAsync_NeverDropsUserMessagesWhenSummaryFails()
    {
        var compactor = new ContextCompactor(new EmptyChatClient());
        var transcript = LongTranscript();

        var outcome = await compactor.CompactAsync(transcript, "No todos.");

        Assert.True(outcome.Compacted);
        Assert.Equal(3, outcome.Transcript.OfType<ChatMessage>().Count(IsUser));
        Assert.Contains(
            outcome.Transcript,
            item => item is ToolResult result
                && result.Text == ContextCompactor.OmittedResultText);
    }

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
