using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionRunnerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Submit_RunsTheTurnAndCollectsIt()
    {
        var client = new ScriptedStreamingClient(TextRound("Hello there."));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        var submitted = runner.SubmitAsync("hi");
        runner.Complete();
        var quit = await runner.RunAsync(CancellationToken.None).WaitAsync(Wait);

        Assert.False(quit);
        Assert.True((await submitted).TurnActive);
        Assert.Single(headless.Observer.Events.OfType<TurnFinished>());
        Assert.Null(headless.Session.TurnTask);
    }

    [Fact]
    public async Task FollowUp_DuringATurnStartsAfterItEnds()
    {
        var client = new ScriptedStreamingClient(TextRound("first"), TextRound("second"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        var first = runner.SubmitAsync("one");
        var second = runner.SubmitAsync("two");
        runner.Complete();
        await runner.RunAsync(CancellationToken.None).WaitAsync(Wait);

        await first;
        await second;
        Assert.Equal(
            ["one", "two"],
            headless.Observer.Events.OfType<UserMessageSent>().Select(sent => sent.Text));
        Assert.Equal(2, headless.Observer.Events.OfType<TurnFinished>().Count());
    }

    [Fact]
    public async Task Input_WhileATurnRunsGoesToTheQueue()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        await runner.SubmitAsync("long task").WaitAsync(Wait);
        await client.Started.WaitAsync(Wait);
        var queued = await runner.SubmitAsync("follow up").WaitAsync(Wait);

        Assert.True(queued.TurnActive);
        Assert.Equal("follow up", Assert.Single(headless.Observer.Events.OfType<QueueChanged>().Last().Items));
        await stop.CancelAsync();
        Assert.False(await running.WaitAsync(Wait));
        Assert.Null(headless.Session.TurnTask);
    }

    [Fact]
    public async Task Quit_EndsTheRunner()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        var submitted = runner.SubmitAsync("/quit");
        var quit = await runner.RunAsync(CancellationToken.None).WaitAsync(Wait);

        Assert.True(quit);
        Assert.True((await submitted).Quit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.SubmitAsync("late"));
    }

    [Fact]
    public async Task QuickPrompt_SendsTheTemplateAsAUserMessage()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        var submitted = runner.SubmitQuickPromptAsync("Review the diff");
        runner.Complete();
        await runner.RunAsync(CancellationToken.None).WaitAsync(Wait);

        Assert.True((await submitted).TurnActive);
        Assert.Equal("Review the diff", Assert.Single(headless.Observer.Events.OfType<UserMessageSent>()).Text);
    }

    [Fact]
    public async Task Prompt_SentWhileACommandRunsStartsAfterIt()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        var status = runner.SubmitAsync("/status");
        var prompt = runner.SubmitAsync("hi");
        runner.Complete();
        await runner.RunAsync(CancellationToken.None).WaitAsync(Wait);
        await status;
        await prompt;

        Assert.Equal("hi", Assert.Single(headless.Observer.Events.OfType<UserMessageSent>()).Text);
        Assert.Single(headless.Observer.Events.OfType<TurnFinished>());
    }

    [Fact]
    public async Task Invoke_RunsAnOperationInOrder()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        var plan = await runner.InvokeAsync(session => session.TogglePlan()).WaitAsync(Wait);

        Assert.Equal(plan, headless.Session.PlanMode);
        await stop.CancelAsync();
        await running.WaitAsync(Wait);
    }

    [Fact]
    public async Task Run_CannotStartTwice()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(CancellationToken.None));
        await stop.CancelAsync();
        Assert.False(await running.WaitAsync(Wait));
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
