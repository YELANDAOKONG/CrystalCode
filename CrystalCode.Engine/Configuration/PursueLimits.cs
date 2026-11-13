using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Bounds for one <c>/pursue start</c> or <c>/pursue resume</c> run. Each run
/// counts from zero, so a resume starts a new allowance. A null field is
/// unlimited, except the stall count, which must stay finite.
/// </summary>
public sealed record PursueLimits
{
    public const int DefaultMaximumContinuations = 1000;

    public const int DefaultMaximumStalls = 10;

    public const int DefaultMaximumModelCalls = 10_000;

    public const int DefaultMaximumToolCalls = 100_000;

    public const int DefaultMaximumDurationSeconds = 7 * 24 * 60 * 60;

    public const int DefaultMaximumWaitSeconds = 3 * 60 * 60;

    public PursueLimits(
        int? maximumContinuations = DefaultMaximumContinuations,
        int maximumStalls = DefaultMaximumStalls,
        int? maximumModelCalls = DefaultMaximumModelCalls,
        int? maximumToolCalls = DefaultMaximumToolCalls,
        int? maximumDurationSeconds = DefaultMaximumDurationSeconds,
        int? maximumWaitSeconds = DefaultMaximumWaitSeconds)
    {
        if (maximumContinuations is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumContinuations),
                maximumContinuations,
                "Maximum continuations cannot be negative.");
        }

        if (maximumStalls < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumStalls),
                maximumStalls,
                "Maximum stalls must be positive.");
        }

        if (maximumWaitSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumWaitSeconds),
                maximumWaitSeconds,
                "Maximum wait seconds must be positive.");
        }

        MaximumContinuations = maximumContinuations;
        MaximumStalls = maximumStalls;
        MaximumModelCalls = maximumModelCalls;
        MaximumToolCalls = maximumToolCalls;
        MaximumDurationSeconds = maximumDurationSeconds;
        MaximumWaitSeconds = maximumWaitSeconds;
        // TurnLimits owns the range rules for model calls, tool calls, and duration.
        _ = ToTurnLimits();
    }

    public static PursueLimits Default { get; } = new();

    public int? MaximumContinuations { get; }

    public int MaximumStalls { get; }

    public int? MaximumModelCalls { get; }

    public int? MaximumToolCalls { get; }

    public int? MaximumDurationSeconds { get; }

    public int? MaximumWaitSeconds { get; }

    /// <summary>
    /// Copies the limits with one field replaced. <paramref name="name"/> is
    /// <c>continuations</c>, <c>stalls</c>, <c>model-calls</c>,
    /// <c>tool-calls</c>, <c>duration</c>, or <c>wait</c>; a null value means
    /// unlimited and is refused for <c>stalls</c>.
    /// </summary>
    public PursueLimits With(string name, int? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name switch
        {
            "continuations" => new PursueLimits(value, MaximumStalls, MaximumModelCalls, MaximumToolCalls, MaximumDurationSeconds, MaximumWaitSeconds),
            "stalls" => new PursueLimits(
                MaximumContinuations,
                value ?? throw new ArgumentException("The stall limit cannot be unlimited.", nameof(value)),
                MaximumModelCalls,
                MaximumToolCalls,
                MaximumDurationSeconds,
                MaximumWaitSeconds),
            "model-calls" => new PursueLimits(MaximumContinuations, MaximumStalls, value, MaximumToolCalls, MaximumDurationSeconds, MaximumWaitSeconds),
            "tool-calls" => new PursueLimits(MaximumContinuations, MaximumStalls, MaximumModelCalls, value, MaximumDurationSeconds, MaximumWaitSeconds),
            "duration" => new PursueLimits(MaximumContinuations, MaximumStalls, MaximumModelCalls, MaximumToolCalls, value, MaximumWaitSeconds),
            "wait" => new PursueLimits(MaximumContinuations, MaximumStalls, MaximumModelCalls, MaximumToolCalls, MaximumDurationSeconds, value),
            _ => throw new ArgumentException(
                "Unknown pursue limit. Use continuations, stalls, model-calls, tool-calls, duration, or wait.",
                nameof(name))
        };
    }

    public TurnLimits ToTurnLimits() =>
        new(
            MaximumModelCalls,
            MaximumToolCalls,
            MaximumDurationSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null);

    public string Describe()
    {
        static string Value(int? value) => value is int number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unlimited";
        return "Pursue limits  continuations " + Value(MaximumContinuations)
            + "  ·  stalls " + MaximumStalls.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "  ·  model calls " + Value(MaximumModelCalls)
            + "  ·  tool calls " + Value(MaximumToolCalls)
            + "  ·  duration " + Value(MaximumDurationSeconds) + (MaximumDurationSeconds is null ? "" : "s")
            + "  ·  wait " + Value(MaximumWaitSeconds) + (MaximumWaitSeconds is null ? "" : "s");
    }
}
