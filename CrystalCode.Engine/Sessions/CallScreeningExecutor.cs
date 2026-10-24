using Crystal.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Turns a call to an unregistered tool, or a call whose input kind does not
/// match its tool, into a failed result for that call alone. Crystal checks the
/// whole batch before running anything and throws on the first bad call, which
/// would drop the valid calls beside it and end the turn. The model can fix a
/// wrong name or input kind when it sees the failure.
/// </summary>
internal sealed class CallScreeningExecutor : IToolExecutor
{
    private readonly IToolExecutor _inner;

    public CallScreeningExecutor(IToolExecutor inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public IReadOnlyList<ToolDefinition> Definitions => _inner.Definitions;

    public async Task<IReadOnlyList<ToolResult>> ExecuteAsync(
        IEnumerable<ToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var batch = calls.ToArray();
        var results = new ToolResult[batch.Length];
        var accepted = new List<(int Index, ToolCall Call)>(batch.Length);
        for (var index = 0; index < batch.Length; index++)
        {
            var refusal = Refusal(batch[index]);
            if (refusal is null)
            {
                accepted.Add((index, batch[index]));
                continue;
            }

            results[index] = new ToolResult(batch[index].CallId, refusal, ToolResultStatus.Failure);
        }

        if (accepted.Count == 0)
        {
            return results;
        }

        var executed = await _inner.ExecuteAsync(
            accepted.Select(static pair => pair.Call),
            cancellationToken);
        if (executed.Count != accepted.Count)
        {
            throw new InvalidOperationException("The tool executor returned an unexpected number of results.");
        }

        for (var position = 0; position < accepted.Count; position++)
        {
            results[accepted[position].Index] = executed[position];
        }

        return results;
    }

    private string? Refusal(ToolCall call)
    {
        var definition = _inner.Definitions.FirstOrDefault(
            candidate => string.Equals(candidate.Name, call.Name, StringComparison.Ordinal));
        if (definition is null)
        {
            return $"Tool '{call.Name}' is not available. Available tools: "
                + string.Join(", ", _inner.Definitions.Select(static tool => tool.Name).Order(StringComparer.Ordinal))
                + ".";
        }

        if (call.InputKind != definition.Input.Kind)
        {
            return definition.Input.Kind == ToolInputKind.Text
                ? $"Tool '{call.Name}' takes plain text input, not JSON arguments."
                : $"Tool '{call.Name}' takes JSON arguments, not plain text input.";
        }

        return null;
    }
}
