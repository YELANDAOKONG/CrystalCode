using System.Diagnostics;
using System.Text.Json;
using System.Text;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Runs one exec tool: operator argv prefix, optional model argv, optional stdin JSON.
/// </summary>
internal sealed class ExecExternalTool : ITool, IMultimodalTool
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly Workspace _workspace;
    private readonly ParsedToolSet _set;
    private readonly ExternalToolSpec _spec;
    private readonly SessionToolHost _host;

    public ExecExternalTool(
        Workspace workspace,
        ParsedToolSet set,
        ExternalToolSpec spec,
        SessionToolHost host)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(host);
        _workspace = workspace;
        _set = set;
        _spec = spec;
        _host = host;
        Definition = new ToolDefinition(spec.Name, spec.Schema, spec.Description);
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        var finished = await RunAsync(call.Arguments, cancellationToken);
        if (finished.Failure is not null)
        {
            return new ToolOutput(finished.Failure, ToolResultStatus.Failure);
        }

        if (_spec.Output == ExternalToolOutputMode.Content)
        {
            return ContentText(finished);
        }

        var text = Combine(finished.Streams);
        var status = finished.ExitCode == 0
            ? ToolResultStatus.Success
            : ToolResultStatus.Failure;
        return new ToolOutput($"exit {finished.ExitCode}\n{text}", status);
    }

    public async ValueTask<MultimodalToolOutput> InvokeAsync(
        MultimodalToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        var finished = await RunAsync(call.Arguments, cancellationToken);
        if (finished.Failure is not null)
        {
            return Failed(finished.Failure);
        }

        if (_spec.Output != ExternalToolOutputMode.Content)
        {
            var text = Combine(finished.Streams);
            var status = finished.ExitCode == 0
                ? MultimodalToolResultStatus.Success
                : MultimodalToolResultStatus.Failure;
            return new MultimodalToolOutput(
                [new TextContent($"exit {finished.ExitCode}\n{text}")],
                status);
        }

        if (!ExecContentOutput.TryParse(
                finished.Streams.Stdout,
                finished.Streams.Stderr,
                out var textBody,
                out var pending,
                out var error))
        {
            return Failed(PrefixExit(finished.ExitCode, error));
        }

        if (finished.ExitCode != 0)
        {
            return Failed(PrefixExit(finished.ExitCode, textBody));
        }

        var loaded = await ExecContentOutput.LoadAsync(pending, _workspace, cancellationToken);
        if (!loaded.Succeeded)
        {
            var failure = finished.Streams.Stderr.Length == 0
                ? loaded.Error
                : loaded.Error + "\n" + finished.Streams.Stderr;
            return Failed(PrefixExit(finished.ExitCode, failure));
        }

        var contents = new List<MultimodalContent> { new TextContent(textBody) };
        contents.AddRange(loaded.Images);
        return new MultimodalToolOutput(contents);
    }

    private ToolOutput ContentText(ExecFinished finished)
    {
        if (!ExecContentOutput.TryParse(
                finished.Streams.Stdout,
                finished.Streams.Stderr,
                out var text,
                out var images,
                out var error))
        {
            return new ToolOutput(PrefixExit(finished.ExitCode, error), ToolResultStatus.Failure);
        }

        if (finished.ExitCode != 0)
        {
            return new ToolOutput(PrefixExit(finished.ExitCode, text), ToolResultStatus.Failure);
        }

        if (images.Count > 0)
        {
            return new ToolOutput(
                "This model cannot accept tool images.",
                ToolResultStatus.Failure);
        }

        return new ToolOutput(text, ToolResultStatus.Success);
    }

    private void ApplyHostEnvironment(ProcessStartInfo start)
    {
        var workspace = string.IsNullOrWhiteSpace(_host.WorkspaceRoot)
            ? _workspace.Root
            : _host.WorkspaceRoot;
        start.Environment["CRYSTAL_WORKSPACE"] = workspace;
        start.Environment["CRYSTAL_SESSION"] = _host.SessionId;
        start.Environment["CRYSTAL_APPROVAL"] = _host.Approval;
    }

    private static string Combine(ProcessOutputReader.Streams streams)
    {
        if (streams.Stderr.Length == 0)
        {
            return streams.Stdout;
        }

        return streams.Stdout.Length == 0
            ? streams.Stderr
            : streams.Stdout + ToolOutputText.LineSeparator + streams.Stderr;
    }

    private static string PrefixExit(int exitCode, string text) =>
        exitCode == 0 ? text : $"exit {exitCode}\n{text}";

    private static MultimodalToolOutput Failed(string text) =>
        new([new TextContent(text)], MultimodalToolResultStatus.Failure);

    private readonly record struct ExecFinished(
        string? Failure,
        int ExitCode,
        ProcessOutputReader.Streams Streams)
    {
        public static ExecFinished Fail(string failure) => new(failure, 0, default);
    }

    private async Task<ExecFinished> RunAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!TryBuildArgv(arguments, out var argv, out var error))
        {
            return ExecFinished.Fail(error);
        }

        if (!TryResolveFileName(argv[0], out var fileName, out error))
        {
            return ExecFinished.Fail(error);
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

        ApplyHostEnvironment(process.StartInfo);

        try
        {
            if (!process.Start())
            {
                return ExecFinished.Fail("The external process failed to start.");
            }
        }
        catch (Exception exception)
        {
            return ExecFinished.Fail(
                "The external process failed to start: " + exception.Message);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_set.TimeoutSeconds is int seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        var outputTask = ProcessOutputReader.ReadStreamsAsync(process, timeout.Token);
        try
        {
            if (_set.Stdin)
            {
                await process.StandardInput.WriteAsync(arguments.AsMemory(), timeout.Token);
                await process.StandardInput.FlushAsync(timeout.Token);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var streams = await outputTask.WaitAsync(timeout.Token);
            return new ExecFinished(null, process.ExitCode, streams);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException) when (_set.TimeoutSeconds is int limit)
        {
            TryKill(process);
            return ExecFinished.Fail(ToolOutputText.Timeout(limit));
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            timeout.Cancel();
            TryKill(process);
            return ExecFinished.Fail(
                "The external process stdin or output failed: " + exception.Message);
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
