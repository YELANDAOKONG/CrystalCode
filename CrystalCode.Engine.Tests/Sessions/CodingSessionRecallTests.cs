using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionRecallTests
{
    [Fact]
    public async Task TryRecall_BeforeOutput_RemovesTheMessageAndKeepsEarlierOnes()
    {
        var client = new HeldStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("keep");

        var running = headless.Session.SubmitAsync("take back", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(headless.Session.RecallPending);
        Assert.True(headless.Session.TryRecall());
        Assert.True(headless.Session.RecallPending);
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.False(await running);

        var recalled = Assert.Single(headless.Observer.Events.OfType<UserMessageRecalled>());
        Assert.Equal("take back", recalled.Text);
        var finished = headless.Observer.Events.OfType<TurnFinished>().Last();
        Assert.Equal(TurnStopReason.Interrupted, finished.Result.StopReason);
        Assert.False(headless.Session.RecallPending);
        Assert.False(headless.Session.TryRecall());

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Items, item => item.Text == "keep");
        Assert.Contains(saved.Archive!, item => item.Text == "keep");
        Assert.DoesNotContain(saved.Items, item => item.Text == "take back");
        Assert.DoesNotContain(saved.Archive!, item => item.Text == "take back");
    }

    [Fact]
    public async Task TryRecall_AfterModelText_LeavesTheMessage()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        var running = headless.Session.SubmitAsync("stay", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(headless.Session.TryRecall());
        Assert.False(headless.Session.RecallPending);
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.False(await running);

        Assert.Empty(headless.Observer.Events.OfType<UserMessageRecalled>());
        var saved = headless.ReadSaved();
        Assert.Contains(saved.Items, item => item.Text == "stay");
        Assert.Contains(saved.Archive!, item => item.Text == "stay");
    }

    [Fact]
    public async Task TryRecall_WhenAFollowUpIsQueued_OnlyInterrupts()
    {
        var client = new HeldStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        var running = headless.Session.SubmitAsync("original", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await headless.Session.SubmitAsync("follow", CancellationToken.None);

        Assert.False(headless.Session.TryRecall());
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.False(await running);
        Assert.True(headless.Session.TurnActive);
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);

        Assert.Empty(headless.Observer.Events.OfType<UserMessageRecalled>());
        var saved = headless.ReadSaved();
        Assert.Contains(saved.Archive!, item => item.Text == "original");
        Assert.Contains(saved.Archive!, item => item.Text == "follow");
    }

    [Fact]
    public async Task TryRecall_WhenAnImageMarkerIsNotHeld_LeavesTheMessage()
    {
        var client = new HeldStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var text = "see " + ImageMarkerText.Tag(4);

        var running = headless.Session.SubmitAsync(text, CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(headless.Session.TryRecall());
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.False(await running);

        Assert.Empty(headless.Observer.Events.OfType<UserMessageRecalled>());
        var saved = headless.ReadSaved();
        Assert.Contains(saved.Archive!, item => item.Text == text);
    }

    [Fact]
    public async Task TryRecall_AfterStatusDuringTheTurn_LeavesTheMessage()
    {
        var client = new HeldStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        var running = headless.Session.SubmitAsync("stay", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await headless.Session.SubmitAsync("/status", CancellationToken.None);

        Assert.False(headless.Session.TryRecall());
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.False(await running);

        Assert.Empty(headless.Observer.Events.OfType<UserMessageRecalled>());
        var saved = headless.ReadSaved();
        Assert.Contains(saved.Archive!, item => item.Text == "stay");
    }

    [Fact]
    public async Task TryRecall_WhenIdle_ReturnsFalse()
    {
        var client = new HeldStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        Assert.False(headless.Session.TryRecall());
        Assert.False(headless.Session.RecallPending);
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
