using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Rewrites declared path arguments, then delegates to an external multimodal tool.
/// An <see cref="IHostMultimodalTool"/> receives a <see cref="ToolHostContext"/> for the call.
/// </summary>
internal sealed class FencedExternalMultimodalTool : IMultimodalTool
{
    private readonly IMultimodalTool _inner;
    private readonly Workspace _workspace;
    private readonly SessionToolHost _host;
    private readonly IReadOnlyList<string> _pathArguments;
    private readonly int? _timeoutSeconds;

    public FencedExternalMultimodalTool(
        IMultimodalTool inner,
        Workspace workspace,
        SessionToolHost host,
        IReadOnlyList<string> pathArguments,
        int? timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(pathArguments);
        _inner = inner;
        _workspace = workspace;
        _host = host;
        _pathArguments = pathArguments;
        _timeoutSeconds = timeoutSeconds;
        Definition = inner.Definition;
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<MultimodalToolOutput> InvokeAsync(
        MultimodalToolCall call,
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
            return new MultimodalToolOutput(
                [new TextContent(error)],
                MultimodalToolResultStatus.Failure);
        }

        var next = rewritten == call.Arguments
            ? call
            : new MultimodalToolCall(
                call.CallId,
                call.Name,
                rewritten,
                call.Contents);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeoutSeconds is int seconds)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        }

        try
        {
            MultimodalToolOutput output;
            if (_inner is IHostMultimodalTool hosted)
            {
                output = await hosted
                    .InvokeAsync(next, _host.CreateContext(), timeout.Token)
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
            var contents = output.Contents.Select(static content =>
                content is TextContent text
                    ? new TextContent(ToolOutputText.Truncate(text.Text))
                    : content);
            return new MultimodalToolOutput(contents, output.Status);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && timeout.IsCancellationRequested
            && _timeoutSeconds is int limit)
        {
            return new MultimodalToolOutput(
                [new TextContent(ToolOutputText.Timeout(limit))],
                MultimodalToolResultStatus.Failure);
        }
    }
}
