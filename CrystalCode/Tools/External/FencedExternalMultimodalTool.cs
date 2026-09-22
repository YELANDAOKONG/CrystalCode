using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Tools.External;

/// <summary>
/// Rewrites declared path arguments, then delegates to an external multimodal tool.
/// </summary>
internal sealed class FencedExternalMultimodalTool : IMultimodalTool
{
    private readonly IMultimodalTool _inner;
    private readonly Workspace _workspace;
    private readonly IReadOnlyList<string> _pathArguments;

    public FencedExternalMultimodalTool(
        IMultimodalTool inner,
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
        var output = await _inner.InvokeAsync(next, cancellationToken);
        var contents = output.Contents.Select(static content =>
            content is TextContent text
                ? new TextContent(ToolOutputText.Truncate(text.Text))
                : content);
        return new MultimodalToolOutput(contents, output.Status);
    }
}
