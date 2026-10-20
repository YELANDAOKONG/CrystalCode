using Crystal.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>Only adjacent task calls overlap; ordinary tools form execution barriers.</summary>
internal sealed class TaskBatchExecutor(IToolExecutor inner) : IToolExecutor
{
    public IReadOnlyList<ToolDefinition> Definitions => inner.Definitions;

    public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(IEnumerable<ToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        var batch = calls.ToArray();
        var results = new List<ToolResult>();
        for (var index = 0; index < batch.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batch[index].Name != Tools.TaskTool.ToolName)
            {
                results.AddRange(await inner.ExecuteAsync([batch[index++]], cancellationToken));
                continue;
            }
            var tasks = new List<Task<IReadOnlyList<ToolResult>>>();
            while (index < batch.Length && batch[index].Name == Tools.TaskTool.ToolName)
            {
                tasks.Add(inner.ExecuteAsync([batch[index++]], cancellationToken));
            }
            foreach (var completed in await Task.WhenAll(tasks))
            {
                results.AddRange(completed);
            }
        }
        return results;
    }
}
