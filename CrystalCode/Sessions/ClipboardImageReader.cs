using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace CrystalCode.Sessions;

/// <summary>Reads image bytes from the OS clipboard without capturing the screen.</summary>
public static class ClipboardImageReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WindowsTimeout = TimeSpan.FromSeconds(20);
    private static readonly string[] ImageTypes =
        ["image/png", "image/jpeg", "image/gif", "image/webp"];
    private static readonly string[] MacTypes =
        ["\"PNGf\"", "JPEG picture", "GIF picture"];

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
            IReadOnlyList<CommandResult> results = OperatingSystem.IsMacOS()
                ? await TryReadMacAsync(cancellationToken)
                : OperatingSystem.IsWindows()
                    ? [await TryReadWindowsAsync(command.FileName, command.Arguments, cancellationToken)]
                    : await TryReadLinuxAsync(command.FileName, cancellationToken);
            foreach (var result in results)
            {
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
        }

        return (null, FailureMessage(
            commandAvailable, imageTooLarge, invalidImage, readerFailed));
    }

    private static IReadOnlyList<(string FileName, string[] Arguments)> Commands()
    {
        if (OperatingSystem.IsWindows())
        {
            const string script = """
                $ErrorActionPreference='Stop'
                Add-Type -AssemblyName System.Windows.Forms
                Add-Type -AssemblyName System.Drawing
                $dataObject=[Windows.Forms.Clipboard]::GetDataObject()
                if ($null -eq $dataObject) { [Console]::Write('NO_IMAGE'); exit 0 }
                $bytes=$null
                if ($dataObject.GetDataPresent('PNG')) {
                    $data=$dataObject.GetData('PNG')
                    if ($data -is [byte[]]) { $bytes=$data }
                    elseif ($data -is [IO.Stream]) {
                        if ($data.Length -gt 20971520) { [Console]::Write('TOO_LARGE'); exit 0 }
                        $data.Position=0
                        $stream=[IO.MemoryStream]::new()
                        try { $data.CopyTo($stream); $bytes=$stream.ToArray() }
                        finally { $stream.Dispose() }
                    }
                }
                if ($null -eq $bytes) {
                    $image=[Windows.Forms.Clipboard]::GetImage()
                    if ($null -eq $image) { [Console]::Write('NO_IMAGE'); exit 0 }
                    $stream=[IO.MemoryStream]::new()
                    try { $image.Save($stream,[Drawing.Imaging.ImageFormat]::Png); $bytes=$stream.ToArray() }
                    finally { $stream.Dispose(); $image.Dispose() }
                }
                if ($bytes.Length -gt 20971520) { [Console]::Write('TOO_LARGE'); exit 0 }
                [Console]::Write([Convert]::ToBase64String($bytes))
                """;
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
                ("wl-paste", ["--list-types"]),
                ("xclip", ["-selection", "clipboard", "-t", "TARGETS", "-o"])
            ];
        }

        return [];
    }

    internal static IReadOnlyList<string> SupportedTypesInOrder(string advertisedTypes) =>
        ImageTypes.Where(type => advertisedTypes.Split('\n', '\r')
            .Any(line => string.Equals(line.Trim(), type, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    internal static IReadOnlyList<string> MacClipboardTypes => MacTypes;

    internal static CommandStatus ClassifyExit(int exitCode, int outputLength) =>
        exitCode != 0 ? CommandStatus.Failed
            : outputLength == 0 ? CommandStatus.Empty : CommandStatus.Success;

    private static async Task<CommandResult> TryReadWindowsAsync(
        string reader,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await TryRunAsync(reader, arguments, true, cancellationToken);
        if (result.Status != CommandStatus.Success || result.Data is null)
        {
            return result;
        }

        var encoded = Encoding.ASCII.GetString(result.Data).Trim();
        if (encoded == "NO_IMAGE")
        {
            return new CommandResult(CommandStatus.Empty, null);
        }

        if (encoded == "TOO_LARGE")
        {
            return new CommandResult(CommandStatus.TooLarge, null);
        }

        try
        {
            var bytes = Convert.FromBase64String(encoded);
            return bytes.Length > ImageFile.MaximumBytes
                ? new CommandResult(CommandStatus.TooLarge, null)
                : new CommandResult(CommandStatus.Success, bytes);
        }
        catch (FormatException)
        {
            return new CommandResult(CommandStatus.Failed, null);
        }
    }

    private static async Task<IReadOnlyList<CommandResult>> TryReadLinuxAsync(
        string reader,
        CancellationToken cancellationToken)
    {
        var listArguments = reader == "wl-paste"
            ? new[] { "--list-types" }
            : ["-selection", "clipboard", "-t", "TARGETS", "-o"];
        var list = await TryRunAsync(reader, listArguments, true, cancellationToken);
        if (list.Status != CommandStatus.Success || list.Data is null)
        {
            return [list];
        }

        var types = SupportedTypesInOrder(Encoding.UTF8.GetString(list.Data));
        if (types.Count == 0)
        {
            return [new CommandResult(CommandStatus.Empty, null)];
        }

        var results = new List<CommandResult>();
        foreach (var type in types)
        {
            string[] arguments = reader == "wl-paste"
                ? ["--no-newline", "--type", type]
                : ["-selection", "clipboard", "-t", type, "-o"];
            var result = await TryRunAsync(reader, arguments, true, cancellationToken);
            results.Add(result);
            if (result.Status is CommandStatus.Success
                or CommandStatus.TooLarge
                or CommandStatus.Missing)
            {
                break;
            }
        }

        return results;
    }

    [SupportedOSPlatform("macos")]
    private static async Task<IReadOnlyList<CommandResult>> TryReadMacAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<CommandResult>();
        foreach (var type in MacTypes)
        {
            var result = await TryReadMacTypeAsync(type, cancellationToken);
            results.Add(result);
            if (result.Status is CommandStatus.Success
                or CommandStatus.TooLarge
                or CommandStatus.Missing)
            {
                break;
            }
        }

        return results;
    }

    [SupportedOSPlatform("macos")]
    private static async Task<CommandResult> TryReadMacTypeAsync(
        string type,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"crystal-clipboard-{Guid.NewGuid():N}.image");
        var escapedPath = path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        // The extra byte reveals an oversized image without writing all of it to disk.
        string[] arguments =
        [
            "-e", "try",
            "-e", $"set imageData to the clipboard as {type}",
            "-e", "on error number errorNumber",
            "-e", "if errorNumber is -1700 or errorNumber is -25133 then return \"NO_IMAGE\"",
            "-e", "error number errorNumber",
            "-e", "end try",
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

            if (result.Data is not null
                && Encoding.UTF8.GetString(result.Data).Trim() == "NO_IMAGE")
            {
                return new CommandResult(CommandStatus.Empty, null);
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
        timeout.CancelAfter(OperatingSystem.IsWindows() ? WindowsTimeout : Timeout);
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

            var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            var maximumOutputBytes = OperatingSystem.IsWindows()
                ? (ImageFile.MaximumBytes + 1) * 4 / 3 + 8
                : ImageFile.MaximumBytes + 1;
            while (true)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(
                    buffer,
                    timeout.Token);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > maximumOutputBytes)
                {
                    TryKill(process);
                    return new CommandResult(CommandStatus.TooLarge, null);
                }

                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            await process.WaitForExitAsync(timeout.Token);
            await stderr;
            var status = ClassifyExit(process.ExitCode, (int)output.Length);
            if (status == CommandStatus.Failed)
            {
                return new CommandResult(CommandStatus.Failed, null);
            }

            if (status == CommandStatus.Empty && expectImageOnOutput)
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

    internal enum CommandStatus
    {
        Missing,
        Empty,
        TooLarge,
        Failed,
        Success
    }

    private sealed record CommandResult(CommandStatus Status, byte[]? Data);
}
