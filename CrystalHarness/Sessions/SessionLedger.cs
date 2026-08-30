using Crystal;

namespace CrystalHarness.Sessions;

/// <summary>
/// Accumulates turn counts and usage for the footer.
/// </summary>
public sealed class SessionLedger
{
    private readonly UsageAccumulator _usage = new();

    public int UserTurns { get; private set; }

    public int ModelCalls { get; private set; }

    public int ToolCalls { get; private set; }

    public void Record(TurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        UserTurns++;
        ModelCalls += result.ModelCallCount;
        ToolCalls += result.ToolCallCount;
        _usage.Add(result.Usage);
    }

    public void Clear()
    {
        UserTurns = 0;
        ModelCalls = 0;
        ToolCalls = 0;
        _usage.Clear();
    }

    public TokenUsage? Usage => _usage.Build();
}
