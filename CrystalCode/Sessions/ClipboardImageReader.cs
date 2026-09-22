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
        if (!OperatingSystem.IsLinux())
        {
            return (null,
                "Clipboard image paste is currently available on Linux with wl-paste or xclip. Use /attach on this platform.");
        }

        foreach (var command in Commands())
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

        return (null,
            "No clipboard image was found. Install wl-paste or xclip, or use /attach <workspace-image-path>.");
    }

    private static IReadOnlyList<(string FileName, string[] Arguments)> Commands() =>
    [
        ("wl-paste", ["--no-newline", "--type", "image/png"]),
        ("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"])
    ];

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
