using System.Diagnostics;
using System.Text;

using Crystal.Tools;

namespace CrystalHarness.Tools;

/// <summary>
/// Runs one shell command in the workspace root after approval.
/// </summary>
public sealed class BashTool : ITool
{
    internal const string ToolName = "bash";

    private readonly Workspace _workspace;

    public BashTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "command": {
                      "type": "string",
                      "description": "The exact shell command to run."
                    }
                  },
                  "required": ["command"]
                }
                """),
            "Runs one shell command after the user approves it. "
            + "Prefer read, glob, and grep for file inspection.");
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (!TryReadCommand(call.Arguments, out var command))
        {
            return new ToolOutput(
                "Arguments must include a command string.",
                ToolResultStatus.Failure);
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = _workspace.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add("-lc");
        process.StartInfo.ArgumentList.Add(command);

        try
        {
            if (!process.Start())
            {
                return new ToolOutput(
                    "The shell process failed to start.",
                    ToolResultStatus.Failure);
            }
        }
        catch (Exception exception)
        {
            return new ToolOutput(
                "The shell process failed to start: " + exception.Message,
                ToolResultStatus.Failure);
        }

        var output = ReadBoundedOutputAsync(process, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(WorkspaceLimits.BashTimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ToolOutput(
                $"The command timed out after {WorkspaceLimits.BashTimeoutSeconds} seconds.",
                ToolResultStatus.Failure);
        }

        var text = await output;
        var status = process.ExitCode == 0
            ? ToolResultStatus.Success
            : ToolResultStatus.Failure;
        return new ToolOutput($"exit {process.ExitCode}\n{text}", status);
    }

    internal static bool TryReadCommand(string arguments, out string command) =>
        ToolArguments.TryReadRequiredString(arguments, "command", out command);

    private static async Task<string> ReadBoundedOutputAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        var builder = new StringBuilder();
        if (stdout.Length > 0)
        {
            builder.Append(stdout);
        }

        if (stderr.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(stderr);
        }

        return ToolOutputText.Truncate(builder.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
