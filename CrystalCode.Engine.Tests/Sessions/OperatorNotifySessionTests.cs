using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// The operator notification command runs at operator waits only, with the
/// documented environment, and never blocks or breaks the session.
/// </summary>
public sealed class OperatorNotifySessionTests
{
    private const string Marker = "notified.txt";

    // Writes "<event>|<tool>|<text>" so one file proves which wait fired.
    private const string Script =
        "printf '%s|%s|%s' \"$CRYSTAL_NOTIFY_EVENT\" \"$CRYSTAL_NOTIFY_TOOL\" \"$CRYSTAL_NOTIFY_TEXT\" > " + Marker;

    [Fact]
    public async Task Approval_RunsTheCommandWithTheCallDetails()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var headless = Create(Notify(events: [OperatorNotifyEvent.Approval]), BashRound(), TextRound("done"));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("make the file");

        var text = await WaitForMarkerAsync(headless);
        Assert.StartsWith("approval|bash|Approval needed: ", text, StringComparison.Ordinal);
        Assert.Equal(1, headless.Approvals.Count);
        Assert.False(File.Exists(Path.Combine(headless.WorkspacePath, "ran.txt")));
    }

    [Fact]
    public async Task Approval_AnsweredWithinTheDelayDoesNotNotify()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var headless = Create(Notify(delaySeconds: 1), BashRound(), TextRound("done"));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("make the file");
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.Equal(1, headless.Approvals.Count);
        Assert.False(File.Exists(Path.Combine(headless.WorkspacePath, Marker)));
    }

    [Fact]
    public async Task Disabled_DoesNotNotifyButTestStillRunsTheCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var headless = Create(Notify().WithEnabled(false), BashRound(), TextRound("done"));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("make the file");
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(File.Exists(Path.Combine(headless.WorkspacePath, Marker)));

        await headless.Session.SubmitAsync("/notify test", CancellationToken.None);

        Assert.Equal("test||Test notification.", await WaitForMarkerAsync(headless));
        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "Operator notification command exited with code 0.");
    }

    [Fact]
    public async Task Turn_NotifiesWhenTheSessionIsIdle()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var headless = Create(Notify(events: [OperatorNotifyEvent.Turn]), TextRound("done"));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("hello");

        Assert.Equal("turn||Turn finished: completed", await WaitForMarkerAsync(headless));
    }

    [Fact]
    public async Task Failure_IsReportedOncePerSession()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var failing = new OperatorNotifySettings(
            true, ["/bin/sh", "-c", "exit 3"], [OperatorNotifyEvent.Approval], 0, 5);
        using var headless = Create(failing, BashRound(), BashRound("c2"), TextRound("done"));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("make the file");
        await WaitUntilAsync(() => Failures(headless).Count > 0);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(2, headless.Approvals.Count);
        var note = Assert.Single(Failures(headless));
        Assert.Contains("exited with code 3", note.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnOff_WritesConfigAndOnNeedsACommand()
    {
        using var empty = new HeadlessSession(new ScriptedStreamingClient());
        await empty.Session.StartAsync(CancellationToken.None);
        await empty.Session.SubmitAsync("/notify on", CancellationToken.None);
        Assert.Contains(
            empty.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("operatorNotify.command", StringComparison.Ordinal));

        using var headless = Create(Notify());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/notify off", CancellationToken.None);
        Assert.False(ReadNotify(headless).GetProperty("enabled").GetBoolean());

        await headless.Session.SubmitAsync("/notify on", CancellationToken.None);
        var saved = ReadNotify(headless);
        Assert.True(saved.GetProperty("enabled").GetBoolean());
        Assert.Equal("/bin/sh", saved.GetProperty("command")[0].GetString());

        await headless.Session.SubmitAsync("/notify", CancellationToken.None);
        var shown = headless.Observer.Events.OfType<NoteWritten>().Last().Text;
        Assert.StartsWith("Operator notification  on", shown, StringComparison.Ordinal);
        Assert.Contains("Command  sh (2 arguments)", shown, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, shown, StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/notify loud", CancellationToken.None);
        Assert.Equal(
            "Notify command must be /notify, /notify on|off, or /notify test.",
            headless.Observer.Events.OfType<ErrorWritten>().Last().Text);
    }

    [Fact]
    public void Store_ReadsAndRoundTripsTheSetting()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Assert.Equal(OperatorNotifySettings.Off, store.Load().OperatorNotify);

        WriteConfig(root, """{ "command": ["notify-send", "Crystal"] }""");
        var implicitOn = store.Load().OperatorNotify;
        Assert.True(implicitOn.Enabled);
        Assert.Equal(["notify-send", "Crystal"], implicitOn.Command);
        Assert.Equal(OperatorNotifySettings.DefaultEvents, implicitOn.Events);

        WriteConfig(root, """
            { "enabled": false, "command": ["notify"], "events": ["turn", "approval"],
              "delaySeconds": 30, "timeoutSeconds": 5 }
            """);
        var loaded = store.Load();
        store.Save(loaded);
        var reloaded = store.Load().OperatorNotify;
        Assert.False(reloaded.Enabled);
        Assert.Equal(["notify"], reloaded.Command);
        Assert.Equal([OperatorNotifyEvent.Turn, OperatorNotifyEvent.Approval], reloaded.Events);
        Assert.Equal(30, reloaded.DelaySeconds);
        Assert.Equal(5, reloaded.TimeoutSeconds);
        using var saved = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.False(saved.RootElement.GetProperty("operatorNotify").GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("""{ "enabled": true }""", "operatorNotify.enabled requires a command array.")]
    [InlineData("""{ "command": ["x"], "events": ["test"] }""", "operatorNotify.events entries must be approval, question, or turn.")]
    [InlineData("""{ "command": ["x", ""] }""", "operatorNotify.command entries cannot be empty.")]
    [InlineData("""{ "command": ["x"], "timeoutSeconds": 0 }""", "operatorNotify.timeoutSeconds must be between 1 and 600.")]
    [InlineData("""{ "command": ["x"], "delaySeconds": -1 }""", "operatorNotify.delaySeconds must be between 0 and 3600.")]
    public void Store_RejectsInvalidValues(string notify, string message)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        WriteConfig(root, notify);

        var error = Assert.Throws<InvalidOperationException>(store.Load);
        Assert.Equal(message, error.Message);
    }

    private static HeadlessSession Create(
        OperatorNotifySettings notify,
        params IReadOnlyList<ChatStreamEvent>[] rounds) =>
        new(
            new ScriptedStreamingClient(rounds),
            settings: HeadlessSession.ScriptedSettings().WithOperatorNotify(notify));

    private static OperatorNotifySettings Notify(
        IReadOnlyList<OperatorNotifyEvent>? events = null,
        int delaySeconds = 0) =>
        new(true, ["/bin/sh", "-c", Script], events, delaySeconds, 5);

    private static List<NoteWritten> Failures(HeadlessSession headless) =>
        [.. headless.Observer.Events.OfType<NoteWritten>()
            .Where(static note => note.Text.StartsWith("Operator notification failed:", StringComparison.Ordinal))];

    private static async Task<string> WaitForMarkerAsync(HeadlessSession headless)
    {
        var path = Path.Combine(headless.WorkspacePath, Marker);
        await WaitUntilAsync(() => File.Exists(path) && new FileInfo(path).Length > 0);
        return await File.ReadAllTextAsync(path);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The notification did not arrive.");
            }

            await Task.Delay(50);
        }
    }

    private static JsonElement ReadNotify(HeadlessSession headless)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(headless.Home.ConfigPath));
        return document.RootElement.GetProperty("operatorNotify").Clone();
    }

    private static void WriteConfig(TemporaryHome root, string notify) =>
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"provider\": \"ollama\", \"model\": \"qwen3:8b\", \"operatorNotify\": " + notify + " }");

    private static ChatStreamEvent[] BashRound(string callId = "c1") =>
    [
        new ChatToolCallDelta(0, 0, callId, "bash", """{"command":"printf ready > ran.txt"}""", ToolInputKind.Json),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
