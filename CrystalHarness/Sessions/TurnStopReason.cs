namespace CrystalHarness.Sessions;

/// <summary>
/// Why one user-message turn stopped.
/// </summary>
public sealed record TurnStopReason
{
    public static TurnStopReason Completed { get; } = new("completed");

    public static TurnStopReason Interrupted { get; } = new("interrupted");

    public static TurnStopReason ModelCallLimitReached { get; } = new("model_call_limit_reached");

    public static TurnStopReason ToolCallLimitReached { get; } = new("tool_call_limit_reached");

    public static TurnStopReason DurationLimitReached { get; } = new("duration_limit_reached");

    public TurnStopReason(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
