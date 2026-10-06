using Crystal.Tools;

using CrystalCode.Plugins.Hooks;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Rewrites tool calls before the host executor approves them, then rewrites results.
/// </summary>
internal sealed class PluginToolExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;
    private readonly PluginHookPipeline _hooks;

    public PluginToolExecutor(IToolExecutor inner, PluginHookPipeline hooks)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(hooks);
        _inner = inner;
        _hooks = hooks;
    }

    public IReadOnlyList<ToolDefinition> Definitions => _inner.Definitions;

    public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(
        IEnumerable<ToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var rewritten = new List<ToolCall>();
        foreach (var call in calls)
        {
            ArgumentNullException.ThrowIfNull(call);
            rewritten.Add(await _hooks.OnToolCallAsync(call, cancellationToken));
        }

        var results = await _inner.ExecuteAsync(rewritten, cancellationToken);
        if (results.Count != rewritten.Count)
        {
            return results;
        }

        var output = new List<ToolResult>(results.Count);
        for (var index = 0; index < results.Count; index++)
        {
            var current = PluginToolResults.From(results[index]);
            var next = await _hooks.OnToolResultAsync(rewritten[index], current, cancellationToken);
            if (next.Images.Count > 0)
            {
                _hooks.ReportIgnoredImages();
                next = new PluginToolResult(next.Text, next.Success);
            }

            output.Add(PluginToolResults.ToText(results[index].CallId, next));
        }

        return output;
    }
}
