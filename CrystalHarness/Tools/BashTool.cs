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

    private const string ToolDescription =
        "Runs one shell command in the workspace root (bash -lc, 120 second timeout). "
        + "Use it for builds, tests, git, and scripts. Do not use it to read, write, or search files; "
        + "use read, glob, grep, edit, and write. Avoid interactive commands. "
        + "Unless the user explicitly asked, do not commit, amend, or push, and do not change git config, "
        + "skip hooks, use interactive git, or force-push.";

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
            ToolDescription);
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
            FileName = ResolveBashFileName(),
            WorkingDirectory = _workspace.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        process.StartInfo.ArgumentList.Add("-lc");
        process.StartInfo.ArgumentList.Add(command);
        if (OperatingSystem.IsWindows())
        {
            // Git Bash login profiles cd to HOME unless this is set.
            process.StartInfo.Environment["CHERE_INVOKING"] = "1";
        }

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

    private static string ResolveBashFileName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        foreach (var candidate in EnumerateWindowsBashCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "bash";
    }

    private static IEnumerable<string> EnumerateWindowsBashCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "Git", "bin", "bash.exe");

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (programFilesX86.Length > 0)
        {
            yield return Path.Combine(programFilesX86, "Git", "bin", "bash.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Programs", "Git", "bin", "bash.exe");

        // System32\bash.exe is the WSL stub; it emits UTF-16 text when no distro exists.
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            string fullDirectory;
            try
            {
                fullDirectory = Path.GetFullPath(trimmed);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                continue;
            }

            if (fullDirectory.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return Path.Combine(fullDirectory, "bash.exe");
        }
    }

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
