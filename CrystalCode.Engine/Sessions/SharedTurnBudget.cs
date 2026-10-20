namespace CrystalCode.Engine.Sessions;

/// <summary>One atomic budget shared by parent and every text or image child run.</summary>
public sealed class SharedTurnBudget
{
    private readonly object _gate = new();
    private readonly TurnLimits _limits;
    private int _modelCalls;
    private int _toolCalls;

    public SharedTurnBudget(TurnLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
    }

    internal int ModelCalls
    {
        get { lock (_gate) { return _modelCalls; } }
    }

    internal int ToolCalls
    {
        get { lock (_gate) { return _toolCalls; } }
    }

    public bool TryModelCall()
    {
        lock (_gate)
        {
            if (_limits.MaximumModelCalls is int max && _modelCalls >= max)
            {
                return false;
            }
            _modelCalls++;
            return true;
        }
    }

    public bool TryToolBatch(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            if (_limits.MaximumToolCalls is int max && count > max - _toolCalls)
            {
                return false;
            }
            _toolCalls = checked(_toolCalls + count);
            return true;
        }
    }
}
