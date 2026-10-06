using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ConversationArchiveTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4];

    [Fact]
    public async Task Turn_KeepsUserAndAssistantTextInTheArchive()
    {
        var client = new ScriptedStreamingClient(TextRound("Hello there."));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("hi");

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Archive!, item => item.Text == "hi");
        Assert.Contains(saved.Archive!, item => item.Text == "Hello there.");
        Assert.Contains(saved.Items, item => item.Text == "hi");
    }

    [Fact]
    public async Task Resume_CopiesAMissingArchiveFromTheSavedItems()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        var resume = new SessionDocument
        {
            Items =
            [
                new SessionItemDocument { Kind = "message", Role = "user", Text = "kept-from-disk" }
            ]
        };
        using var headless = new HeadlessSession(client, resume);
        await headless.Session.StartAsync(CancellationToken.None);

        var replayed = headless.Observer.Events.OfType<HistoryReplayed>().Single();
        Assert.Contains(
            replayed.Items.OfType<ChatMessage>(),
            message => message.Text == "kept-from-disk");

        await headless.RunTurnAsync("next");

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Archive!, item => item.Text == "kept-from-disk");
        Assert.Contains(saved.Archive!, item => item.Text == "next");
    }

    [Fact]
    public async Task Compact_LeavesTheArchiveAndShortensTheModelContext()
    {
        var head = "HEADTOKEN" + new string('a', 70_000);
        var resume = new SessionDocument
        {
            ImageMarkersTagged = true,
            Items =
            [
                new SessionItemDocument { Kind = "message", Role = "system", Text = "system" },
                new SessionItemDocument { Kind = "message", Role = "user", Text = head },
                new SessionItemDocument { Kind = "message", Role = "user", Text = "TAILTOKEN" }
            ],
            Archive =
            [
                new SessionItemDocument { Kind = "message", Role = "user", Text = head },
                new SessionItemDocument { Kind = "message", Role = "user", Text = "TAILTOKEN" }
            ]
        };
        using var headless = new HeadlessSession(new SummaryClient(), resume);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("/compact");

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Archive!, item => item.Text is not null && item.Text.Contains("HEADTOKEN", StringComparison.Ordinal));
        Assert.Contains(saved.Archive!, item => item.Text == "TAILTOKEN");
        Assert.Contains(
            saved.Items,
            item => item.Text is not null && item.Text.Contains(CompactionPrompt.Marker, StringComparison.Ordinal));
        Assert.DoesNotContain(
            saved.Items,
            item => item.Text is not null && item.Text.Contains("HEADTOKEN", StringComparison.Ordinal));
        Assert.Contains(saved.Items, item => item.Text == "TAILTOKEN");
    }

    [Fact]
    public async Task Save_KeepsAnImageReferencedOnlyByTheArchive()
    {
        var marker = ImageMarkerText.Tag(1);
        var resume = new SessionDocument
        {
            ImageMarkersTagged = true,
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "tail" }],
            Archive = [new SessionItemDocument { Kind = "message", Role = "user", Text = "shot " + marker }],
            Images = [new SessionImageDocument { Number = 1, MimeType = "image/png", Data = Png }]
        };
        using var headless = new HeadlessSession(new ScriptedStreamingClient(TextRound("ok")), resume);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("again");

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Images, image => image.Number == 1);
        Assert.Contains(
            saved.Archive!,
            item => item.Text is not null && item.Text.Contains(marker, StringComparison.Ordinal));
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private sealed class SummaryClient : IStreamingChatClient
    {
        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only compacts.");

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new ChatResponse(
                [
                    new ChatCandidate(
                        [new ChatMessage(ChatRole.Assistant, "kept the facts")],
                        FinishReason.Stop)
                ]));
        }
    }
}
