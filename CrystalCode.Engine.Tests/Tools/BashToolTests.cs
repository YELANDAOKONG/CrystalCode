using System.Diagnostics;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class BashToolTests
{
    [Fact]
    public async Task InvokeAsync_RunsCommandInWorkspaceRoot()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "note.txt"), "hello");
        var tool = new BashTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"cat note.txt"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.StartsWith("exit 0", output.Text);
        Assert.Contains("hello", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_ReportsNonZeroExit()
    {
        using var root = new TemporaryWorkspace();
        var tool = new BashTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"exit 7"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.StartsWith("exit 7", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_TimesOutWhenTheCommandExceedsTheLimit()
    {
        using var root = new TemporaryWorkspace();
        var tool = new BashTool(new Workspace(root.Path), timeoutSeconds: 1);

        var output = await tool.InvokeAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"sleep 30"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("timed out after 1 second", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_UnlimitedTimeoutRunsTheCommand()
    {
        using var root = new TemporaryWorkspace();
        var tool = new BashTool(new Workspace(root.Path), timeoutSeconds: null);

        var output = await tool.InvokeAsync(
            new ToolCall("1", BashTool.ToolName, """{"command":"sleep 0.2"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("no per-command timeout", tool.Definition.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_CancelStopsTheProcess()
    {
        using var root = new TemporaryWorkspace();
        var pidFile = Path.Combine(root.Path, "pid.txt");
        var tool = new BashTool(new Workspace(root.Path), timeoutSeconds: null);
        using var cancellation = new CancellationTokenSource();
        var running = tool.InvokeAsync(
            new ToolCall(
                "1",
                BashTool.ToolName,
                """{"command":"echo $$ > pid.txt; sleep 30"}"""),
            cancellation.Token).AsTask();

        var started = DateTimeOffset.UtcNow;
        while (!File.Exists(pidFile) && DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(50);
        }

        Assert.True(File.Exists(pidFile));
        var pid = int.Parse(File.ReadAllText(pidFile).Trim());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Task.Delay(300);

        Assert.False(IsAlive(pid));
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
