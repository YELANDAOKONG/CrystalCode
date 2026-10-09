using Crystal.Tools;

using CrystalCode.Engine.Home;
using CrystalCode.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Rewrites declared path arguments, then delegates to the inner tool.
/// An <see cref="IHostTool"/> receives a <see cref="ToolHostContext"/> for the call.
/// </summary>
internal sealed class FencedExternalTool : ITool
{
    private readonly ITool _inner;
    private readonly Workspace _workspace;
    private readonly SessionToolHost _host;
    private readonly string _directoryName;
    private readonly IReadOnlyList<string> _pathArguments;
    private readonly int? _timeoutSeconds;

    public FencedExternalTool(
        ITool inner,
        Workspace workspace,
        SessionToolHost host,
        string directoryName,
        IReadOnlyList<string> pathArguments,
        int? timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentNullException.ThrowIfNull(pathArguments);
        _inner = inner;
        _workspace = workspace;
        _host = host;
        _directoryName = directoryName;
        _pathArguments = pathArguments;
        _timeoutSeconds = timeoutSeconds;
        Definition = inner.Definition;
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!ArgumentPathRewriter.TryRewrite(
                call.Arguments,
                _pathArguments,
                _workspace,
                out var rewritten,
                out var error))
        {
            return new ToolOutput(error, ToolResultStatus.Failure);
        }

        var next = rewritten == call.Arguments
            ? call
            : new ToolCall(call.CallId, call.Name, rewritten);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeoutSeconds is int seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        try
        {
            ToolOutput output;
            if (_inner is IHostTool hosted)
            {
                output = await hosted
                    .InvokeAsync(
                        next,
                        _host.CreateContext(ExtensionDataKind.Tools, _directoryName),
                        timeout.Token)
                    .AsTask()
                    .WaitAsync(timeout.Token);
            }
            else
            {
                output = await _inner
                    .InvokeAsync(next, timeout.Token)
                    .AsTask()
                    .WaitAsync(timeout.Token);
            }

            return new ToolOutput(ToolOutputText.Truncate(output.Text), output.Status);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && timeout.IsCancellationRequested
            && _timeoutSeconds is int limit)
        {
            return new ToolOutput(
                ToolOutputText.Timeout(limit),
                ToolResultStatus.Failure);
        }
    }
}
