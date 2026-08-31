using Crystal.Tools;

namespace CrystalCode.Tools.External;

/// <summary>
/// Rewrites declared path arguments, then delegates to the inner tool.
/// </summary>
internal sealed class FencedExternalTool : ITool
{
    private readonly ITool _inner;
    private readonly Workspace _workspace;
    private readonly IReadOnlyList<string> _pathArguments;

    public FencedExternalTool(
        ITool inner,
        Workspace workspace,
        IReadOnlyList<string> pathArguments)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(pathArguments);
        _inner = inner;
        _workspace = workspace;
        _pathArguments = pathArguments;
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
        var output = await _inner.InvokeAsync(next, cancellationToken);
        return new ToolOutput(ToolOutputText.Truncate(output.Text), output.Status);
    }
}
