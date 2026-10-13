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
        await Task.Delay(300);
        Assert.False(IsAlive(pid));
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
