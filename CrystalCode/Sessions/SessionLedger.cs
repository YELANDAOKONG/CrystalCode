using Crystal;

namespace CrystalCode.Sessions;

/// <summary>
/// Session counters, last-request usage, and cumulative provider usage.
/// </summary>
public sealed class SessionLedger
{
    private bool _cumulativeUsageComplete = true;

    public int UserTurns { get; private set; }

    public int ModelCalls { get; private set; }

    public int ToolCalls { get; private set; }

    public TokenUsage? Usage { get; private set; }

    public TokenUsage? CumulativeUsage { get; private set; }

    public void Record(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RecordUsage(result);
        UserTurns++;
        ModelCalls += result.ModelCallCount;
        ToolCalls += result.ToolCallCount;
        if (result.Usage is not null)
        {
            Usage = result.Usage;
        }
    }

    public void ReplaceUsage(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        Usage = usage;
    }

    public void Restore(
        int userTurns,
        int modelCalls,
        int toolCalls,
        TokenUsage? usage,
        TokenUsage? cumulativeUsage = null)
    {
        if (userTurns < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userTurns));
        }

        if (modelCalls < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modelCalls));
        }

        if (toolCalls < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toolCalls));
        }

        UserTurns = userTurns;
        ModelCalls = modelCalls;
        ToolCalls = toolCalls;
        Usage = usage;
        CumulativeUsage = cumulativeUsage;
        _cumulativeUsageComplete = modelCalls == 0 || cumulativeUsage is not null;
    }

    public void Clear()
    {
        UserTurns = 0;
        ModelCalls = 0;
        ToolCalls = 0;
        Usage = null;
        CumulativeUsage = null;
        _cumulativeUsageComplete = true;
    }

    private void RecordUsage(TurnResult result)
    {
        if (!_cumulativeUsageComplete || result.ModelCallCount == 0)
        {
            return;
        }

        var turnUsage = result.AccumulatedUsage;
        if (turnUsage is null && result.ModelCallCount == 1)
        {
            turnUsage = result.Usage;
        }

        if (turnUsage is null)
        {
            CumulativeUsage = null;
            _cumulativeUsageComplete = false;
            return;
        }

        CumulativeUsage = Add(CumulativeUsage, turnUsage);
    }

    internal static TokenUsage Add(TokenUsage? current, TokenUsage added)
    {
        if (current is null)
        {
            return added;
        }

        long? reasoning = current.ReasoningTokenCount is long currentReasoning
            && added.ReasoningTokenCount is long addedReasoning
                ? checked(currentReasoning + addedReasoning)
                : null;
        return new TokenUsage(
            checked(current.InputTokenCount + added.InputTokenCount),
            checked(current.OutputTokenCount + added.OutputTokenCount),
            reasoning);
    }
}
