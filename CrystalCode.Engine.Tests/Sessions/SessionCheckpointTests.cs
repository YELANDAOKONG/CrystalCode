using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// The running turn is saved after the user message, each model round, and
/// each tool batch, so a killed process keeps everything but the work in
/// flight.
/// </summary>
public sealed class SessionCheckpointTests
{
    [Fact]
    public async Task RunningTurn_SavesTheUserMessageBeforeTheFirstModelRound()
    {
        var client = new HeldStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        _ = headless.Session.SubmitAsync("first prompt", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Items, item => item.Role == "user" && item.Text == "first prompt");
        Assert.Contains(saved.Archive!, item => item.Role == "user" && item.Text == "first prompt");
        await StopAsync(headless);
    }

    [Fact]
    public async Task RunningTurn_SavesCompletedToolBatchesWhileTheNextRoundIsInFlight()
    {
        var client = new HeldStreamingClient(ToolRound("todoread", "{}"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        _ = headless.Session.SubmitAsync("inspect", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        var saved = headless.ReadSaved();
        var call = Assert.Single(saved.Items, item => item.Kind == "tool_call");
        var result = Assert.Single(saved.Items, item => item.Kind == "tool_result");
        Assert.Equal(call.CallId, result.CallId);
        Assert.Contains(saved.Archive!, item => item.Kind == "tool_result" && item.CallId == call.CallId);
        Assert.Equal(1, saved.ToolCalls);

        using var resumed = new HeadlessSession(
            new ScriptedStreamingClient(TextRound("resumed")),
            saved);
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("continue");
        Assert.Contains(resumed.ReadSaved().Items, item => item.Text == "resumed");
        await StopAsync(headless);
    }

    [Fact]
    public async Task RecalledPrompt_IsRemovedFromTheCheckpoint()
    {
        var client = new HeldStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("keep");

        var running = headless.Session.SubmitAsync("take back", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(headless.ReadSaved().Items, item => item.Text == "take back");

        Assert.True(headless.Session.TryRecall());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.False(await running);

        var saved = headless.ReadSaved();
        Assert.Contains(saved.Items, item => item.Text == "keep");
        Assert.DoesNotContain(saved.Items, item => item.Text == "take back");
        Assert.DoesNotContain(saved.Archive!, item => item.Text == "take back");
    }

    [Fact]
    public async Task UnwritableSessionDirectory_ReportsOnceAndTheTurnStillCompletes()
    {
        if (OperatingSystem.IsWindows())
        {
            // Directory modes are POSIX; Windows ACLs are not exercised here.
            return;
        }

        using var headless = new HeadlessSession(new ScriptedStreamingClient(
            ToolRound("todoread", "{}"),
            TextRound("done")));
        await headless.Session.StartAsync(CancellationToken.None);
        headless.Home.EnsureCreated();
        var sessions = headless.Home.SessionsDirectory;
        File.SetUnixFileMode(sessions, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (CanWrite(sessions))
            {
                // Running as root ignores directory modes.
                return;
            }

            var quit = await headless.Session.SubmitAsync("inspect", CancellationToken.None);
            Assert.False(quit);
            await headless.Session.TurnTask!;
        }
        finally
        {
            File.SetUnixFileMode(
                sessions,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.Single(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "Session checkpoint could not be saved");
        var finished = headless.Observer.Events.OfType<TurnFinished>().Last();
        Assert.Equal(TurnStopReason.Completed, finished.Result.StopReason);
        Assert.Contains(headless.ReadSaved().Items, item => item.Text == "done");
    }

    [Fact]
    public async Task ShutdownCollect_SavesTheInterruptedTurnAndCloseKeepsIt()
    {
        var client = new HeldStreamingClient(ToolRound("todoread", "{}"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        _ = headless.Session.SubmitAsync("long task", CancellationToken.None);
        await client.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        // The terminal's shutdown path: interrupt, collect without a token, close.
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.FinishTurnAsync(CancellationToken.None);
        var closure = headless.Session.Close();

        Assert.True(closure.Saved);
        var saved = headless.ReadSaved();
        Assert.Contains(saved.Items, item => item.Role == "user" && item.Text == "long task");
        Assert.Contains(saved.Items, item => item.Kind == "tool_result" && item.CallId == "call-1");
        Assert.Equal(1, saved.UserTurns);
    }

    private static async Task StopAsync(HeadlessSession headless)
    {
        headless.Session.TryInterrupt();
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
    }

    private static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, ".probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static ChatStreamEvent[] ToolRound(string name, string arguments) =>
    [
        new ChatToolCallDelta(0, 0, "call-1", name, arguments, ToolInputKind.Json),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];
}
