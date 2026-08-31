using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Tools.External;

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

        try
        {
            if (_set.Stdin)
            {
                await process.StandardInput.WriteAsync(call.Arguments);
                await process.StandardInput.FlushAsync(cancellationToken);
            }

            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return new ToolOutput(
                "The external process stdin failed: " + exception.Message,
                ToolResultStatus.Failure);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_set.TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ToolOutput(
                $"The command timed out after {_set.TimeoutSeconds} seconds.",
                ToolResultStatus.Failure);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
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

        var text = ToolOutputText.Truncate(builder.ToString());
        var status = process.ExitCode == 0
            ? ToolResultStatus.Success
            : ToolResultStatus.Failure;
        return new ToolOutput($"exit {process.ExitCode}\n{text}", status);
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
    }
}
