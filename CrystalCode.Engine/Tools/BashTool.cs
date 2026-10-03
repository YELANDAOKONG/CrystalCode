using System.Diagnostics;
using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Runs one shell command in the workspace root after approval.
/// </summary>
public sealed class BashTool : ITool
{
    public const string ToolName = "bash";

    private readonly Workspace _workspace;
    private readonly int? _timeoutSeconds;

    public BashTool(Workspace workspace, int? timeoutSeconds = WorkspaceLimits.BashTimeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!IsSupported(timeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds),
                timeoutSeconds,
                "Bash timeout must be a positive supported number of seconds, or unlimited.");
        }

        _workspace = workspace;
        _timeoutSeconds = timeoutSeconds;
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
            Describe(timeoutSeconds));
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        if (_timeoutSeconds is int seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        var output = ProcessOutputReader.ReadAsync(process, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var text = await output.WaitAsync(timeout.Token);
            var status = process.ExitCode == 0
                ? ToolResultStatus.Success
                : ToolResultStatus.Failure;
            return new ToolOutput($"exit {process.ExitCode}\n{text}", status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException) when (_timeoutSeconds is int limit)
        {
            TryKill(process);
            return new ToolOutput(
                ToolOutputText.Timeout(limit),
                ToolResultStatus.Failure);
        }
        finally
        {
            if (!HasExited(process))
            {
                TryKill(process);
            }

            if (timeout.IsCancellationRequested)
            {
                try
                {
                    await output;
                }
                catch (Exception exception) when (exception is OperationCanceledException
                    or IOException
                    or ObjectDisposedException)
                {
                }
            }
        }
    }

    internal static bool IsSupported(int? timeoutSeconds) =>
        timeoutSeconds is null
        || timeoutSeconds is int seconds
            && seconds > 0
            && seconds <= WorkspaceLimits.MaximumBashTimeoutSeconds;

    internal static bool TryReadCommand(string arguments, out string command) =>
        ToolArguments.TryReadRequiredString(arguments, "command", out command);

    private static string Describe(int? timeoutSeconds)
    {
        var limit = timeoutSeconds is int seconds
            ? $"{seconds} second timeout"
            : "no per-command timeout";
        return "Runs one shell command in the workspace root (bash -lc, " + limit + "). "
            + "Use it for builds, tests, git, and scripts. Do not use it to read, write, or search files; "
            + "use read, glob, grep, edit, and write. Avoid interactive commands. "
            + "Unless the user explicitly asked, do not commit, amend, or push, and do not change git config, "
            + "skip hooks, use interactive git, or force-push.";
    }

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

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
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
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
