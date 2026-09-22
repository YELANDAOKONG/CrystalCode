using System.ComponentModel;
using System.Diagnostics;

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

        foreach (var command in commands)
        {
            var data = await TryRunAsync(command.FileName, command.Arguments, cancellationToken);
            if (data is not { Length: > 0 })
            {
                continue;
            }

            var mimeType = ImageFile.DetectMimeType(data);
            if (mimeType is not null)
            {
                return (new ImageAttachment(number, mimeType, data), string.Empty);
            }
        }

        return (null, FailureMessage());
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
            return [("pngpaste", ["-"])];
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

    private static string FailureMessage()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "No clipboard image was found. Install pngpaste, or use /attach <workspace-image-path>.";
        }

        if (OperatingSystem.IsLinux())
        {
            return "No clipboard image was found. Install wl-paste or xclip, or use /attach <workspace-image-path>.";
        }

        return "No clipboard image was found. Use /attach <workspace-image-path>.";
    }

    private static async Task<byte[]?> TryRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
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
                return null;
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
                    return null;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            await process.WaitForExitAsync(timeout.Token);
            await stderr;
            if (process.ExitCode != 0
                || output.Length == 0)
            {
                return null;
            }

            return output.ToArray();
        }
        catch (Exception exception) when (exception is Win32Exception
            or IOException
            or InvalidOperationException
            or OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
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
}
