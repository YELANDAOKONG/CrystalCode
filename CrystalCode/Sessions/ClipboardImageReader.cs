using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace CrystalCode.Sessions;

/// <summary>Reads image bytes from the OS clipboard without capturing the screen.</summary>
public static class ClipboardImageReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<(ImageAttachment? Image, string Error)> TryReadAsync(
        int number,
        CancellationToken cancellationToken)
    {
        var commands = Commands();
        if (commands.Count == 0)
        {
            return (null,
                "Clipboard image paste is not available on this operating system. Use /attach <workspace-image-path>.");
        }

        var commandAvailable = false;
        var imageTooLarge = false;
        var invalidImage = false;
        var readerFailed = false;
        foreach (var command in commands)
        {
            var result = OperatingSystem.IsMacOS()
                ? await TryReadMacAsync(cancellationToken)
                : await TryRunAsync(command.FileName, command.Arguments, true, cancellationToken);
            if (result.Status == CommandStatus.Missing)
            {
                continue;
            }

            commandAvailable = true;
            if (result.Status == CommandStatus.TooLarge)
            {
                imageTooLarge = true;
                continue;
            }

            if (result.Status == CommandStatus.Failed)
            {
                readerFailed = true;
                continue;
            }

            if (result.Data is not { Length: > 0 } data)
            {
                continue;
            }

            var mimeType = ImageFile.DetectMimeType(data);
            if (mimeType is not null)
            {
                return (new ImageAttachment(number, mimeType, data), string.Empty);
            }

            invalidImage = true;
        }

        return (null, FailureMessage(
            commandAvailable, imageTooLarge, invalidImage, readerFailed));
    }

    private static IReadOnlyList<(string FileName, string[] Arguments)> Commands()
    {
        if (OperatingSystem.IsWindows())
        {
            const string script =
                "Add-Type -AssemblyName System.Windows.Forms; "
                + "Add-Type -AssemblyName System.Drawing; "
                + "$image=[Windows.Forms.Clipboard]::GetImage(); "
                + "if ($null -eq $image) { exit 1 }; "
                + "$stream=[IO.MemoryStream]::new(); "
                + "try { $image.Save($stream,[Drawing.Imaging.ImageFormat]::Png); "
                + "[Console]::OpenStandardOutput().Write($stream.GetBuffer(),0,[int]$stream.Length) } "
                + "finally { $stream.Dispose(); $image.Dispose() }";
            return
            [
                ("powershell.exe", ["-NoProfile", "-NonInteractive", "-STA", "-Command", script]),
                ("pwsh.exe", ["-NoProfile", "-NonInteractive", "-STA", "-Command", script])
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return [("osascript", [])];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                ("wl-paste", ["--no-newline", "--type", "image/png"]),
                ("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"])
            ];
        }

        return [];
    }

    [SupportedOSPlatform("macos")]
    private static async Task<CommandResult> TryReadMacAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"crystal-clipboard-{Guid.NewGuid():N}.png");
        var escapedPath = path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        // The extra byte reveals an oversized image without writing all of it to disk.
        string[] arguments =
        [
            "-e", "set imageData to the clipboard as \"PNGf\"",
            "-e", $"set fileRef to open for access (POSIX file \"{escapedPath}\") with write permission",
            "-e", "set eof fileRef to 0",
            "-e", $"write imageData to fileRef for {ImageFile.MaximumBytes + 1}",
            "-e", "close access fileRef"
        ];

        try
        {
            // AppleScript writes into this owner-only file rather than creating a public temp file.
            using (new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
            {
            }

            var result = await TryRunAsync("osascript", arguments, false, cancellationToken);
            if (result.Status != CommandStatus.Success)
            {
                return result;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > ImageFile.MaximumBytes)
            {
                return new CommandResult(CommandStatus.TooLarge, null);
            }

            if (stream.Length == 0)
            {
                return new CommandResult(CommandStatus.Empty, null);
            }

            var data = new byte[stream.Length];
            await stream.ReadExactlyAsync(data, cancellationToken);
            return new CommandResult(CommandStatus.Success, data);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return new CommandResult(CommandStatus.Failed, null);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string FailureMessage(
        bool commandAvailable,
        bool imageTooLarge,
        bool invalidImage,
        bool readerFailed)
    {
        if (imageTooLarge)
        {
            return "Clipboard image exceeds the 20 MiB host limit. Use a smaller image.";
        }

        if (invalidImage)
        {
            return "Clipboard image is not a supported PNG, JPEG, GIF, or WebP image.";
        }

        if (!commandAvailable)
        {
            if (OperatingSystem.IsMacOS())
            {
                return "The clipboard reader (osascript) is unavailable. Use /attach <workspace-image-path>.";
            }

            if (OperatingSystem.IsWindows())
            {
                return "No PowerShell clipboard reader is available. Use /attach <workspace-image-path>.";
            }

            return "No clipboard image reader is available. Install wl-paste or xclip, or use /attach <workspace-image-path>.";
        }

        if (readerFailed)
        {
            return "Could not read a clipboard image. Use /attach <workspace-image-path>.";
        }

        return "No clipboard image was found. Use /attach <workspace-image-path>.";
    }

    private static async Task<CommandResult> TryRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        bool expectImageOnOutput,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(Timeout);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new CommandResult(CommandStatus.Failed, null);
            }

            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(
                    buffer,
                    timeout.Token);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > ImageFile.MaximumBytes)
                {
                    TryKill(process);
                    return new CommandResult(CommandStatus.TooLarge, null);
                }

                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            await process.WaitForExitAsync(timeout.Token);
            await stderr;
            if (process.ExitCode != 0 || (expectImageOnOutput && output.Length == 0))
            {
                return new CommandResult(CommandStatus.Empty, null);
            }

            return new CommandResult(CommandStatus.Success, output.ToArray());
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            return new CommandResult(CommandStatus.Missing, null);
        }
        catch (Exception exception) when (exception is Win32Exception
            or IOException
            or InvalidOperationException
            or OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new CommandResult(CommandStatus.Failed, null);
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
        catch (Exception exception) when (exception is InvalidOperationException
            or NotSupportedException
            or Win32Exception)
        {
        }
    }

    private enum CommandStatus
    {
        Missing,
        Empty,
        TooLarge,
        Failed,
        Success
    }

    private sealed record CommandResult(CommandStatus Status, byte[]? Data);
}
