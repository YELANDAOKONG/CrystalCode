using System.Diagnostics;
using System.Text;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Creates and stops the shell processes owned by the built-in Bash tools.
/// </summary>
internal static class BashProcess
{
    public static ProcessStartInfo CreateStartInfo(
        string workspaceRoot,
        string command,
        bool redirectStandardInput = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveBashFileName(),
            WorkingDirectory = workspaceRoot,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(command);
        if (OperatingSystem.IsWindows())
        {
            // Git Bash login profiles cd to HOME unless this is set.
            startInfo.Environment["CHERE_INVOKING"] = "1";
        }

        return startInfo;
    }

    public static bool HasExited(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public static void TryKill(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
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
}
