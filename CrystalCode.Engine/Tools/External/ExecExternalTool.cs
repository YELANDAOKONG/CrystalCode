using System.Diagnostics;
using System.Text.Json;
using System.Text;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Runs one exec tool: operator argv prefix, optional model argv, optional stdin JSON.
/// </summary>
internal sealed class ExecExternalTool : ITool
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly Workspace _workspace;
    private readonly ParsedToolSet _set;
    private readonly ExternalToolSpec _spec;

    public ExecExternalTool(Workspace workspace, ParsedToolSet set, ExternalToolSpec spec)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(spec);
        _workspace = workspace;
        _set = set;
        _spec = spec;
        Definition = new ToolDefinition(spec.Name, spec.Schema, spec.Description);
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (!TryBuildArgv(call.Arguments, out var argv, out var error))
        {
            return new ToolOutput(error, ToolResultStatus.Failure);
        }

        if (!TryResolveFileName(argv[0], out var fileName, out error))
        {
            return new ToolOutput(error, ToolResultStatus.Failure);
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = _workspace.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (_set.Stdin)
        {
            process.StartInfo.StandardInputEncoding = Utf8NoBom;
        }
        for (var index = 1; index < argv.Count; index++)
        {
            process.StartInfo.ArgumentList.Add(argv[index]);
        }

        try
        {
            if (!process.Start())
            {
                return new ToolOutput(
                    "The external process failed to start.",
                    ToolResultStatus.Failure);
            }
        }
        catch (Exception exception)
        {
            return new ToolOutput(
                "The external process failed to start: " + exception.Message,
                ToolResultStatus.Failure);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_set.TimeoutSeconds is int seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        var outputTask = ProcessOutputReader.ReadAsync(process, timeout.Token);
        try
        {
            if (_set.Stdin)
            {
                await process.StandardInput.WriteAsync(call.Arguments.AsMemory(), timeout.Token);
                await process.StandardInput.FlushAsync(timeout.Token);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var text = await outputTask.WaitAsync(timeout.Token);
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
        catch (OperationCanceledException) when (_set.TimeoutSeconds is int limit)
        {
            TryKill(process);
            return new ToolOutput(
                ToolOutputText.Timeout(limit),
                ToolResultStatus.Failure);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            timeout.Cancel();
            TryKill(process);
            return new ToolOutput(
                "The external process stdin or output failed: " + exception.Message,
                ToolResultStatus.Failure);
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }

            if (timeout.IsCancellationRequested)
            {
                try
                {
                    await outputTask;
                }
                catch (Exception exception) when (
                    exception is OperationCanceledException or IOException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private bool TryBuildArgv(
        string arguments,
        out List<string> argv,
        out string error)
    {
        argv = [.. _set.Command, .. _spec.CommandSuffix];
        error = string.Empty;
        if (argv.Count == 0)
        {
            error = "exec requires a command array.";
            return false;
        }

        if (_spec.Argv.Count == 0)
        {
            return true;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(arguments);
        }
        catch (JsonException)
        {
            error = "Arguments must be a JSON object.";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Arguments must be a JSON object.";
                return false;
            }

            foreach (var pair in _spec.Argv)
            {
                if (!document.RootElement.TryGetProperty(pair.Key, out var property))
                {
                    continue;
                }

                if (!TryAppendValue(argv, pair.Value, property, out error))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryAppendValue(
        List<string> argv,
        string flag,
        JsonElement property,
        out string error)
    {
        error = string.Empty;
        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                var text = property.GetString();
                if (text is null)
                {
                    error = $"Argument for '{flag}' must be a scalar.";
                    return false;
                }

                argv.Add(flag);
                argv.Add(text);
                return true;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                argv.Add(flag);
                argv.Add(property.ToString());
                return true;
            case JsonValueKind.Array:
                foreach (var item in property.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || item.GetString() is null)
                    {
                        error = $"Argument for '{flag}' arrays must contain strings.";
                        return false;
                    }

                    argv.Add(flag);
                    argv.Add(item.GetString()!);
                }

                return true;
            default:
                error = $"Argument for '{flag}' must be a string, number, boolean, or string array.";
                return false;
        }
    }

    private bool TryResolveFileName(string fileName, out string resolved, out string error)
    {
        resolved = fileName;
        error = string.Empty;
        if (fileName.IndexOfAny(['/', '\\']) < 0 && !Path.IsPathRooted(fileName))
        {
            var local = Path.Combine(_set.Directory, fileName);
            if (File.Exists(local) && ExternalPath.IsInside(_set.Directory, local))
            {
                resolved = local;
            }

            return true;
        }

        string full;
        try
        {
            full = Path.IsPathRooted(fileName)
                ? Path.GetFullPath(fileName)
                : Path.GetFullPath(Path.Combine(_set.Directory, fileName));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "Executable path is not valid.";
            return false;
        }

        if (!Path.IsPathRooted(fileName) && !ExternalPath.IsInside(_set.Directory, full))
        {
            error = "Executable path must stay inside the tool set directory.";
            return false;
        }

        resolved = full;
        return true;
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
