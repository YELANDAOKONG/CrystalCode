using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Operator tool sets load off the calling thread, report each set by name
/// while the loading activity is up, and a plain reload can be interrupted.
/// </summary>
public sealed class LoadingProgressTests
{
    [Fact]
    public async Task Start_ReportsEachToolSetInsideTheLoadingActivity()
    {
        using var harness = new HeadlessSession(
            new ScriptedStreamingClient([]),
            prepare: static (_, workspace) =>
            {
                WriteToolSet(workspace, "alpha");
                WriteToolSet(workspace, "beta");
            });

        await harness.Session.StartAsync(CancellationToken.None);

        var events = harness.Observer.Events;
        var start = IndexOf(events, static item => item is ActivityChanged { Activity: SessionActivity.LoadingTools });
        var end = IndexOf(events, static item => item is ActivityChanged { Activity: SessionActivity.Idle }, start);
        var loaded = events
            .Select(static (item, index) => (item, index))
            .Where(static pair => pair.item is LoadingProgressed { Kind: LoadingKind.ToolSet })
            .ToArray();

        Assert.Equal(
            ["alpha", "beta"],
            loaded.Select(static pair => ((LoadingProgressed)pair.item).Name).Order(StringComparer.Ordinal));
        Assert.All(loaded, pair => Assert.InRange(pair.index, start + 1, end - 1));
    }

    [Fact]
    public async Task Start_WithoutToolSetsReportsNoItems()
    {
        using var harness = new HeadlessSession(new ScriptedStreamingClient([]));

        await harness.Session.StartAsync(CancellationToken.None);

        Assert.DoesNotContain(harness.Observer.Events, static item => item is LoadingProgressed);
    }

    [Fact]
    public async Task ToolsReload_RunsTheLoadOffTheCallingThread()
    {
        using var gate = new ManualResetEventSlim();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new HeadlessSession(
            new ScriptedStreamingClient([]),
            prepare: static (_, workspace) => WriteToolSet(workspace, "slow"));
        await harness.Session.StartAsync(CancellationToken.None);
        harness.Observer.Clear();
        harness.Observer.Hook = item => Hold(item is LoadingProgressed, blocked, gate);

        // The load blocks in its progress callback until the gate opens. Had
        // it run on this thread, SubmitAsync would not return before that.
        var reload = harness.Session.SubmitAsync("/tools reload", CancellationToken.None);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(reload.IsCompleted);
        gate.Set();
        await reload;
        Assert.Contains(harness.Observer.Events, static item => item is NoteWritten { Text: "Tools reloaded" });
    }

    [Fact]
    public async Task ToolsReload_InterruptKeepsThePreviousCatalog()
    {
        using var gate = new ManualResetEventSlim();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new HeadlessSession(
            new ScriptedStreamingClient([]),
            prepare: static (_, workspace) =>
            {
                WriteToolSet(workspace, "alpha");
                WriteToolSet(workspace, "beta");
            });
        await harness.Session.StartAsync(CancellationToken.None);
        harness.Observer.Clear();
        harness.Observer.Hook = item => Hold(item is LoadingProgressed { Name: "alpha" }, blocked, gate);
        var reload = harness.Session.SubmitAsync("/tools reload", CancellationToken.None);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(harness.Session.TryInterrupt());
        gate.Set();
        await reload;
        harness.Observer.Hook = null;

        var events = harness.Observer.Events;
        Assert.DoesNotContain(events, static item => item is LoadingProgressed { Name: "beta" });
        Assert.DoesNotContain(events, static item => item is NoteWritten { Text: "Tools reloaded" });
        Assert.Contains(events, static item => item is NoteWritten { Text: "Reload cancelled. The previous catalog stays loaded." });
        Assert.Equal(SessionActivity.Idle, events.OfType<ActivityChanged>().Last().Activity);
        Assert.False(harness.Session.TryInterrupt());

        harness.Observer.Clear();
        await harness.Session.SubmitAsync("/tools", CancellationToken.None);
        var listed = Assert.Single(harness.Observer.Events.OfType<ToolsListed>());
        Assert.Contains(listed.Report.External, static tool => tool.Name == "alpha");
        Assert.Contains(listed.Report.External, static tool => tool.Name == "beta");
    }

    private static void Hold(bool match, TaskCompletionSource blocked, ManualResetEventSlim gate)
    {
        if (!match)
        {
            return;
        }

        blocked.TrySetResult();
        gate.Wait(TimeSpan.FromSeconds(10));
    }

    private static int IndexOf(IReadOnlyList<SessionEvent> events, Func<SessionEvent, bool> match, int from = 0)
    {
        for (var index = from; index < events.Count; index++)
        {
            if (match(events[index]))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Event not found.");
    }

    private static void WriteToolSet(string workspace, string name)
    {
        var directory = Path.Combine(workspace, ".crystal", "tools", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Placeholder {{name}}.",
              "schema": { "type": "object", "properties": {} },
              "command": ["unused-{{name}}"]
            }
            """);
    }
}
