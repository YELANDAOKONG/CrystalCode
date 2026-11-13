using Xunit;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class TitleCommandTests
{
    [Fact]
    public void Title_IsItsOwnVerbWithTheTextAsArgument()
    {
        Assert.True(SessionCommand.TryParse("/title clear", out var command));

        Assert.Equal(SessionVerb.Title, command.Verb);
        Assert.Equal("clear", command.Argument);
    }

    [Fact]
    public void Topic_ParsesUnchanged()
    {
        Assert.True(SessionCommand.TryParse("/topic clear", out var command));

        Assert.Equal(SessionVerb.Topic, command.Verb);
        Assert.Equal("clear", command.Argument);
    }

    [Fact]
    public void Menu_TitleHasItsOwnEntryWithoutTopicSubcommands()
    {
        var commands = SlashMenu.Create(null, topicArguments:
        [
            new("clear", "Clear the topic", ["clear"]),
            new("on", "Enable naming", ["on"])
        ]);

        var title = Assert.Single(commands, command => command.Name == "title");
        Assert.Equal(["title"], title.Keys);
        Assert.Empty(title.ArgumentOptions);
        var topic = Assert.Single(commands, command => command.Name == "topic");
        Assert.Equal(["topic"], topic.Keys);
        Assert.Equal(2, topic.ArgumentOptions.Count);
    }

    [Theory]
    [InlineData("clear", "clear")]
    [InlineData("on", "on")]
    [InlineData("off", "off")]
    [InlineData("model", "model")]
    [InlineData("set My title", "set My title")]
    [InlineData("\"Parser   cleanup.\"", "Parser cleanup")]
    [InlineData("修复登录问题", "修复登录问题")]
    public async Task Title_RenamesAndPersistsWithoutCallingTheModel(string text, string expected)
    {
        var model = new ScriptedStreamingClient(Text("Done."));
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("First message");
        headless.Observer.Clear();

        await headless.Session.SubmitAsync("/title " + text, CancellationToken.None);

        Assert.Empty(headless.Observer.Events.OfType<ErrorWritten>());
        Assert.Empty(headless.Observer.Events.OfType<TurnStarted>());
        Assert.Empty(headless.Observer.Events.OfType<UserMessageSent>());
        Assert.Equal(1, model.RequestCount);
        Assert.Equal(expected, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Topic);
        var saved = headless.ReadSaved();
        Assert.Equal(expected, saved.Topic);
        Assert.Equal("operator", saved.TopicSource);
        Assert.Equal(expected, Assert.Single(new SessionStore(headless.Home).List(headless.WorkspacePath)).Title);
    }

    [Theory]
    [InlineData("/title")]
    [InlineData("/title   ")]
    [InlineData("/title \"\"...")]
    public async Task InvalidTitle_KeepsTheCurrentName(string command)
    {
        var model = new ScriptedStreamingClient();
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/title Original", CancellationToken.None);
        headless.Observer.Clear();

        await headless.Session.SubmitAsync(command, CancellationToken.None);

        Assert.Equal("Use /title <text>.", Assert.Single(headless.Observer.Events.OfType<ErrorWritten>()).Text);
        Assert.Empty(headless.Observer.Events.OfType<ChromeChanged>());
        await headless.Session.SubmitAsync("/topic", CancellationToken.None);
        Assert.Contains(headless.Observer.Events.OfType<NoteWritten>(), note => note.Text == "Topic  Original");
        Assert.Equal(0, model.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusyTitle_IsRefusedWithoutInterruptingOrQueueing(bool enqueue)
    {
        var model = new BlockingStreamingClient();
        using var headless = new HeadlessSession(model);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/title Original", CancellationToken.None);
        await headless.Session.SubmitAsync("Keep working", CancellationToken.None);
        await model.Started.WaitAsync(TimeSpan.FromSeconds(10));
        headless.Observer.Clear();
        try
        {
            if (enqueue)
            {
                await headless.Session.EnqueueAsync("/title Changed", CancellationToken.None);
            }
            else
            {
                await headless.Session.SubmitAsync("/title Changed", CancellationToken.None);
            }

            Assert.Equal("Finish the current turn before using /title.", Assert.Single(headless.Observer.Events.OfType<ErrorWritten>()).Text);
            Assert.False(headless.Session.TurnTask!.IsCompleted);
            Assert.Empty(headless.Observer.Events.OfType<ChromeChanged>());
            Assert.Empty(headless.Observer.Events.OfType<QueueChanged>());
        }
        finally
        {
            headless.Session.TryInterrupt();
            await headless.Session.FinishTurnAsync(CancellationToken.None);
        }

        Assert.Equal("Original", headless.ReadSaved().Topic);
    }

    [Fact]
    public async Task Title_CancelsPendingAutomaticNamingAndKeepsTheOperatorName()
    {
        var model = new HeldTopicStreamingClient("Automatic name");
        var settings = HeadlessSession.ScriptedSettings().WithTopicModel(new TopicModelSettings(true));
        using var headless = new HeadlessSession(model, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("Inspect the parser");
        await model.TopicStarted.WaitAsync(TimeSpan.FromSeconds(10));

        await headless.Session.SubmitAsync("/title Manual name", CancellationToken.None);
        model.Release();
        await headless.Session.TopicTask;

        Assert.Equal("Manual name", headless.ReadSaved().Topic);
        Assert.Equal("operator", headless.ReadSaved().TopicSource);
    }

    private static ChatStreamEvent[] Text(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
