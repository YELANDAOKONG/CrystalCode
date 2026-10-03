namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Optional bounds for one user-message turn. Null means unlimited.
/// </summary>
public sealed record TurnLimits
{
    private static readonly TimeSpan MaximumSupportedDuration =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public const int DefaultMaximumModelCalls = 1024;

    public const int DefaultMaximumToolCalls = 8192;

    public static readonly TimeSpan DefaultMaximumDuration = TimeSpan.FromDays(7);

    public static TurnLimits Unlimited { get; } = new(null, null, null);

    public TurnLimits(
        int? maximumModelCalls,
        int? maximumToolCalls,
        TimeSpan? maximumDuration)
    {
        if (maximumModelCalls is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumModelCalls),
                maximumModelCalls,
                "Maximum model calls must be positive.");
        }

        if (maximumToolCalls is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumToolCalls),
                maximumToolCalls,
                "Maximum tool calls cannot be negative.");
        }

        if (maximumDuration is TimeSpan duration
            && (duration <= TimeSpan.Zero || duration > MaximumSupportedDuration))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration),
                maximumDuration,
                "Maximum duration must be positive and supported by the runtime timer.");
        }

        MaximumModelCalls = maximumModelCalls;
        MaximumToolCalls = maximumToolCalls;
        MaximumDuration = maximumDuration;
    }

    public int? MaximumModelCalls { get; }

    public int? MaximumToolCalls { get; }

    public TimeSpan? MaximumDuration { get; }

    public static TurnLimits CreateDefault() =>
        new(DefaultMaximumModelCalls, DefaultMaximumToolCalls, DefaultMaximumDuration);
}
