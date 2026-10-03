using Crystal.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Rewrites declared path arguments, then delegates to the inner tool.
/// </summary>
internal sealed class FencedExternalTool : ITool
{
    private readonly ITool _inner;
    private readonly Workspace _workspace;
    private readonly IReadOnlyList<string> _pathArguments;
    private readonly int? _timeoutSeconds;

    public FencedExternalTool(
        ITool inner,
        Workspace workspace,
        IReadOnlyList<string> pathArguments,
        int? timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(pathArguments);
        _inner = inner;
        _workspace = workspace;
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
            var output = await _inner.InvokeAsync(next, timeout.Token)
                .AsTask()
                .WaitAsync(timeout.Token);
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
