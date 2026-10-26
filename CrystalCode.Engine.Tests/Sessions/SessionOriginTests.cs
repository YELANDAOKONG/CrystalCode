using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionOriginTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00];

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Origin_MarksReportsButNotTurnOutput()
    {
        var client = new ScriptedStreamingClient(TextRound("Hello there."));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);
        await runner.SubmitAsync("/status", "client-a").WaitAsync(Wait);
        await runner.SubmitAsync("hi", "client-b").WaitAsync(Wait);
        runner.Complete();
        await running.WaitAsync(Wait);

        var events = headless.Observer.Events;
        Assert.Equal("client-a", Assert.Single(events.OfType<StatusReported>()).Origin);
        Assert.Equal("client-b", Assert.Single(events.OfType<UserMessageSent>()).Origin);
        Assert.All(events.OfType<StreamReceived>(), streamed => Assert.Null(streamed.Origin));
        Assert.Null(Assert.Single(events.OfType<TurnFinished>()).Origin);
        Assert.All(
            events.Where(value => value is SessionStarted or PreferencesChanged),
            value => Assert.Null(value.Origin));
    }

    [Fact]
    public async Task DraftEdit_DiscardsOnlyThatOriginsImages()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: VisionSettings(),
            images: new ChildVisionClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        var first = await runner.InvokeAsync(session => Attach(session), "client-a").WaitAsync(Wait);
        var second = await runner.InvokeAsync(session => Attach(session), "client-b").WaitAsync(Wait);
        await runner.InvokeAsync(session =>
        {
            session.NotifyDraftChanged(string.Empty);
            return true;
        }, "client-a").WaitAsync(Wait);

        Assert.False(headless.Session.TryGetImage(first, out _));
        Assert.True(headless.Session.TryGetImage(second, out _));
        await stop.CancelAsync();
        await running.WaitAsync(Wait);
    }

    [Fact]
    public async Task Recall_ReturnsTheMessageToTheSendingOrigin()
    {
        var client = new HeldStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        await runner.SubmitAsync("take it back", "client-a").WaitAsync(Wait);
        await client.Entered.WaitAsync(Wait);
        Assert.True(headless.Session.TryRecall());
        await WaitForAsync(() => headless.Observer.Events.OfType<UserMessageRecalled>().Any());

        Assert.Equal("client-a", Assert.Single(headless.Observer.Events.OfType<UserMessageRecalled>()).Origin);
        await stop.CancelAsync();
        await running.WaitAsync(Wait);
    }

    [Fact]
    public void BeginOrigin_RestoresThePreviousOrigin()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        using (headless.Session.BeginOrigin("outer"))
        {
            using (headless.Session.BeginOrigin("inner"))
            {
                Assert.Equal("inner", headless.Session.CurrentOrigin);
            }

            Assert.Equal("outer", headless.Session.CurrentOrigin);
        }

        Assert.Null(headless.Session.CurrentOrigin);
    }

    private static int Attach(CodingSession session)
    {
        Assert.True(session.TryAttachImage(Png, out var image, out var error), error);
        return image!.Number;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met.");
            }

            await Task.Delay(10);
        }
    }

    private static HarnessSettings VisionSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(200_000, imageInput: true) });
        return new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8, new ProviderCatalog([provider]));
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
