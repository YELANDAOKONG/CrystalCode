using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class BackgroundBashToolTests
{
    [Fact]
    public async Task StartStatusAndLogs_ReportTaskLifecycle()
    {
        using var root = new TemporaryWorkspace();
        var registry = new BackgroundTaskRegistry(new Workspace(root.Path), timeoutSeconds: null);
        var start = new BackgroundBashStartTool(registry);

        var started = await start.InvokeAsync(
            new ToolCall("1", BackgroundBashStartTool.ToolName, """{"command":"printf ready"}"""));

        Assert.Equal(ToolResultStatus.Success, started.Status);
        var taskId = ReadValue(started.Text, "task_id");
        var status = new BackgroundBashStatusTool(registry);
        var logs = new BackgroundBashLogsTool(registry);

        var statusOutput = await WaitForStatusAsync(status, taskId, BackgroundTaskStatus.Exited);
        Assert.Contains("exit_code: 0", statusOutput.Text, StringComparison.Ordinal);

        var logsOutput = await logs.InvokeAsync(
            new ToolCall(
                "2",
                BackgroundBashLogsTool.ToolName,
                $$"""{"taskId":"{{taskId}}","limit":100}"""));

        Assert.Equal(ToolResultStatus.Success, logsOutput.Status);
        Assert.Contains("ready", logsOutput.Text, StringComparison.Ordinal);
        Assert.Contains("next_offset:", logsOutput.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stop_TerminatesRunningTask()
    {
        using var root = new TemporaryWorkspace();
        var registry = new BackgroundTaskRegistry(new Workspace(root.Path), timeoutSeconds: null);
        var start = new BackgroundBashStartTool(registry);
        var stop = new BackgroundBashStopTool(registry);
        var pidFile = Path.Combine(root.Path, "pid.txt");

        var started = await start.InvokeAsync(
            new ToolCall(
                "1",
                BackgroundBashStartTool.ToolName,
                """{"command":"echo $$ > pid.txt; sleep 30"}"""));
        var taskId = ReadValue(started.Text, "task_id");
        var startedAt = DateTimeOffset.UtcNow;
        while (!File.Exists(pidFile) && DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(50);
        }

        Assert.True(File.Exists(pidFile));
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        var stopped = await stop.InvokeAsync(
            new ToolCall(
                "2",
                BackgroundBashStopTool.ToolName,
                $$"""{"taskId":"{{taskId}}"}"""));

        Assert.Equal(ToolResultStatus.Success, stopped.Status);
        Assert.Contains("status: Stopped", stopped.Text, StringComparison.Ordinal);
        Assert.False(IsAlive(pid));
    }

    [Fact]
    public async Task CompletedTasks_DoNotConsumeActiveTaskSlots()
    {
        using var root = new TemporaryWorkspace();
        var registry = new BackgroundTaskRegistry(new Workspace(root.Path), timeoutSeconds: null);

        for (var index = 0; index <= WorkspaceLimits.MaximumBackgroundTasks; index++)
        {
            Assert.True(
                registry.TryStart(
                    "printf done",
                    out var snapshot,
                    out var error),
                error);
            await WaitForTerminalStatusAsync(registry, snapshot.TaskId);
        }
    }

    [Fact]
    public async Task Logs_RetainNewestOutputWhenTheBufferOverflows()
    {
        using var root = new TemporaryWorkspace();
        var registry = new BackgroundTaskRegistry(new Workspace(root.Path), timeoutSeconds: null);
        var command = $"printf '%*s' {WorkspaceLimits.MaximumBackgroundTaskOutputCharacters} ''; printf newest";

        Assert.True(registry.TryStart(command, out var started, out var error), error);
        var snapshot = await WaitForTerminalStatusAsync(registry, started.TaskId);
        var offset = Math.Max(snapshot.LogStartOffset, snapshot.LogEndOffset - 100);

        Assert.True(registry.TryReadLogs(
            snapshot.TaskId,
            offset,
            100,
            out var log));
        Assert.Contains("newest", log.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackgroundProcess_ReceivesClosedStandardInput()
    {
        using var root = new TemporaryWorkspace();
        var registry = new BackgroundTaskRegistry(new Workspace(root.Path), timeoutSeconds: null);

        Assert.True(
            registry.TryStart(
                "if read value; then printf unexpected; else printf stdin-closed; fi",
                out var started,
                out var error),
            error);
        var snapshot = await WaitForTerminalStatusAsync(registry, started.TaskId);

        Assert.True(registry.TryReadLogs(
            snapshot.TaskId,
            snapshot.LogStartOffset,
            100,
            out var log));
        Assert.Contains("stdin-closed", log.Text, StringComparison.Ordinal);
    }

    private static async Task<ToolOutput> WaitForStatusAsync(
        BackgroundBashStatusTool status,
        string taskId,
        BackgroundTaskStatus expected)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5))
        {
            var output = await status.InvokeAsync(
                new ToolCall(
                    "2",
                    BackgroundBashStatusTool.ToolName,
                    $$"""{"taskId":"{{taskId}}"}"""));
            if (output.Text.Contains($"status: {expected}", StringComparison.Ordinal))
            {
                return output;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("The background task did not reach the expected status.");
    }

    private static async Task<BackgroundTaskSnapshot> WaitForTerminalStatusAsync(
        BackgroundTaskRegistry registry,
        string taskId)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5))
        {
            Assert.True(registry.TryGetSnapshot(taskId, out var snapshot));
            if (snapshot.Status is BackgroundTaskStatus.Exited
                or BackgroundTaskStatus.Failed
                or BackgroundTaskStatus.TimedOut
                or BackgroundTaskStatus.Stopped)
            {
                return snapshot;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("The background task did not reach a terminal status.");
    }

    private static string ReadValue(string text, string name)
    {
        var prefix = name + ": ";
        var line = text.Split('\n').Single(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
