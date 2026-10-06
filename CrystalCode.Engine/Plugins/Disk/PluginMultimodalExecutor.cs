using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Rewrites multimodal calls before the host executor approves them, then rewrites results.
/// </summary>
internal sealed class PluginMultimodalExecutor : IMultimodalToolExecutor
{
    private readonly IMultimodalToolExecutor _inner;
    private readonly PluginHookPipeline _hooks;

    public PluginMultimodalExecutor(IMultimodalToolExecutor inner, PluginHookPipeline hooks)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(hooks);
        _inner = inner;
        _hooks = hooks;
    }

    public IReadOnlyList<ToolDefinition> Definitions => _inner.Definitions;

    public async Task<IReadOnlyList<MultimodalToolResult>> ExecuteAsync(
        IEnumerable<MultimodalToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var rewritten = new List<MultimodalToolCall>();
        foreach (var call in calls)
        {
            ArgumentNullException.ThrowIfNull(call);
            var next = await _hooks.OnToolCallAsync(
                new ToolCall(call.CallId, call.Name, call.Arguments),
                cancellationToken);
            rewritten.Add(new MultimodalToolCall(next.CallId, next.Name, next.Arguments, call.Contents));
        }

        var results = await _inner.ExecuteAsync(rewritten, cancellationToken);
        if (results.Count != rewritten.Count)
        {
            return results;
        }

        var output = new List<MultimodalToolResult>(results.Count);
        for (var index = 0; index < results.Count; index++)
        {
            var call = new ToolCall(rewritten[index].CallId, rewritten[index].Name, rewritten[index].Arguments);
            var next = await _hooks.OnToolResultAsync(
                call,
                PluginToolResults.From(results[index]),
                cancellationToken);
            var notes = new List<string>();
            output.Add(PluginToolResults.ToMultimodal(results[index].CallId, next, notes));
            foreach (var note in notes)
            {
                _hooks.ReportDetail(note);
            }
        }

        return output;
    }
}
