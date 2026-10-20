using Crystal.Tools;

namespace CrystalCode.Engine.Sessions;

internal sealed class ChildToolExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;
    private readonly HashSet<string> _allowed;

    public ChildToolExecutor(IToolExecutor inner, IEnumerable<string> names)
    {
        _inner = inner;
        _allowed = names.ToHashSet(StringComparer.Ordinal);
        Definitions = inner.Definitions.Where(definition => _allowed.Contains(definition.Name)).ToArray();
    }

    public IReadOnlyList<ToolDefinition> Definitions { get; }

    public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ToolResult>();
        foreach (var call in calls)
        {
            if (!_allowed.Contains(call.Name))
            {
                results.Add(new ToolResult(call.CallId, "This tool is not available to this child Agent.", ToolResultStatus.Failure));
                continue;
            }
            results.AddRange(await _inner.ExecuteAsync([call], cancellationToken));
        }
        return results;
    }
}
