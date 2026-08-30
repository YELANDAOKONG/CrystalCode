namespace CrystalHarness.Sessions;

/// <summary>
/// Bounds for one user-message turn.
/// </summary>
public sealed record TurnLimits
{
    public const int DefaultMaximumModelCalls = 32;

    public const int DefaultMaximumToolCalls = 64;

    public static readonly TimeSpan DefaultMaximumDuration = TimeSpan.FromMinutes(15);

    public TurnLimits(
        int maximumModelCalls,
        int maximumToolCalls,
        TimeSpan maximumDuration)
    {
        if (maximumModelCalls <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumModelCalls),
                maximumModelCalls,
                "Maximum model calls must be positive.");
        }

        if (maximumToolCalls <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumToolCalls),
                maximumToolCalls,
                "Maximum tool calls must be positive.");
        }

        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration),
                maximumDuration,
                "Maximum duration must be positive.");
        }

        MaximumModelCalls = maximumModelCalls;
        MaximumToolCalls = maximumToolCalls;
        MaximumDuration = maximumDuration;
    }

    public int MaximumModelCalls { get; }

    public int MaximumToolCalls { get; }

    public TimeSpan MaximumDuration { get; }

    public static TurnLimits CreateDefault() =>
        new(DefaultMaximumModelCalls, DefaultMaximumToolCalls, DefaultMaximumDuration);
}
