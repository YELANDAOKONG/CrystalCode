using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;
using CrystalCode.Approvals;
using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Executes optional image-producing tools and delegates all other calls
/// to the existing approved text executor.
/// </summary>
public sealed class HybridMultimodalToolExecutor : IMultimodalToolExecutor
{
    private readonly IToolExecutor _textExecutor;
    private readonly IReadOnlyDictionary<string, IMultimodalTool> _tools;
    private readonly ApprovalPolicy _approval;

    public HybridMultimodalToolExecutor(
        IToolExecutor textExecutor,
        IEnumerable<IMultimodalTool> tools,
        ApprovalPolicy approval)
    {
        ArgumentNullException.ThrowIfNull(textExecutor);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(approval);
        _textExecutor = textExecutor;
        _approval = approval;
        var registered = new Dictionary<string, IMultimodalTool>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (!registered.TryAdd(tool.Definition.Name, tool))
            {
                throw new ArgumentException(
                    $"Multimodal tool '{tool.Definition.Name}' is registered more than once.",
                    nameof(tools));
            }
        }

        _tools = registered;
        var definitions = new List<ToolDefinition>(textExecutor.Definitions);
        var names = new HashSet<string>(
            definitions.Select(static definition => definition.Name),
            StringComparer.Ordinal);
        foreach (var tool in registered.Values)
        {
            if (names.Add(tool.Definition.Name))
            {
                definitions.Add(tool.Definition);
            }
        }

        Definitions = definitions.AsReadOnly();
    }

    public IReadOnlyList<ToolDefinition> Definitions { get; }

    public async Task<IReadOnlyList<MultimodalToolResult>> ExecuteAsync(
        IEnumerable<MultimodalToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var results = new List<MultimodalToolResult>();
        foreach (var call in calls)
        {
            if (_tools.TryGetValue(call.Name, out var tool))
            {
                results.Add(await ExecuteMultimodalAsync(
                    call,
                    tool,
                    cancellationToken));
                continue;
            }

            var textCall = new ToolCall(call.CallId, call.Name, call.Arguments);
            var textResults = await _textExecutor.ExecuteAsync(
                [textCall],
                cancellationToken);
            var result = textResults[0];
            results.Add(new MultimodalToolResult(
                result.CallId,
                [new TextContent(result.Text)],
                result.Status == ToolResultStatus.Success
                    ? MultimodalToolResultStatus.Success
                    : MultimodalToolResultStatus.Failure));
        }

        return results;
    }

    private async Task<MultimodalToolResult> ExecuteMultimodalAsync(
        MultimodalToolCall call,
        IMultimodalTool tool,
        CancellationToken cancellationToken)
    {
        if (call.Contents.Count != 0)
        {
            return Failed(call, "Model-generated tool content is not supported yet.");
        }

        var textCall = new ToolCall(call.CallId, call.Name, call.Arguments);
        var decision = await _approval.DecideAsync(textCall, cancellationToken);
        if (decision.Action == ToolInvocationAction.Reject)
        {
            var text = decision.RejectionOutput?.Text
                ?? "The tool invocation was rejected.";
            return Failed(call, text);
        }

        try
        {
            var output = await tool.InvokeAsync(call, cancellationToken);
            return new MultimodalToolResult(call.CallId, output.Contents, output.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var mapped = await HarnessExceptionMapper.MapAsync(
                textCall,
                exception,
                cancellationToken);
            return Failed(
                call,
                mapped?.Text ?? $"Tool {call.Name} failed unexpectedly.");
        }
    }

    private static MultimodalToolResult Failed(
        MultimodalToolCall call,
        string text) =>
        new(
            call.CallId,
            [new TextContent(text)],
            MultimodalToolResultStatus.Failure);
}
