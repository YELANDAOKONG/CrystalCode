using Crystal;

namespace CrystalHarness.Sessions;

/// <summary>
/// Session turn counts and the last provider-reported usage snapshot.
/// </summary>
public sealed class SessionLedger
{
    public int UserTurns { get; private set; }

    public int ModelCalls { get; private set; }

    public int ToolCalls { get; private set; }

    public TokenUsage? Usage { get; private set; }

    public void Record(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
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

    public void Restore(int userTurns, int modelCalls, int toolCalls, TokenUsage? usage)
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
    }

    public void Clear()
    {
        UserTurns = 0;
        ModelCalls = 0;
        ToolCalls = 0;
        Usage = null;
    }
}
