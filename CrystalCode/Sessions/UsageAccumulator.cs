using Crystal;

namespace CrystalCode.Sessions;

/// <summary>
/// Last provider-reported usage snapshot and the summed turn total.
/// CTX and compaction use <see cref="Last"/> (one model round). <see cref="Build"/>
/// stays the sum of rounds for turn accounting.
/// </summary>
public sealed class UsageAccumulator
{
    private bool _hasUsage;
    private bool _complete = true;
    private bool _completeReasoning = true;
    private long _inputTokenCount;
    private long _outputTokenCount;
    private long _reasoningTokenCount;

    public TokenUsage? Last { get; private set; }

    public void Add(TokenUsage? usage)
    {
        if (usage is null)
        {
            _complete = false;
            return;
        }

        _hasUsage = true;
        Last = usage;
        _inputTokenCount = checked(_inputTokenCount + usage.InputTokenCount);
        _outputTokenCount = checked(_outputTokenCount + usage.OutputTokenCount);
        if (usage.ReasoningTokenCount is long reasoning)
        {
            _reasoningTokenCount = checked(_reasoningTokenCount + reasoning);
        }
        else
        {
            _completeReasoning = false;
        }
    }

    public void Clear()
    {
        _hasUsage = false;
        _complete = true;
        _completeReasoning = true;
        _inputTokenCount = 0;
        _outputTokenCount = 0;
        _reasoningTokenCount = 0;
        Last = null;
    }

    public TokenUsage? Build()
    {
        if (!_hasUsage || !_complete)
        {
            return null;
        }

        return new TokenUsage(
            _inputTokenCount,
            _outputTokenCount,
            _completeReasoning ? _reasoningTokenCount : null);
    }
}
