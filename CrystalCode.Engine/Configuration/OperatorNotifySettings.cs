namespace CrystalCode.Engine.Configuration;

/// <summary>
/// An operator-authored command the host runs when the session waits for the
/// operator. There is no built-in command. Omitted means off.
/// </summary>
public sealed record OperatorNotifySettings
{
    public const int DefaultDelaySeconds = 0;

    public const int MaximumDelaySeconds = 3600;

    public const int DefaultTimeoutSeconds = 10;

    public const int MaximumTimeoutSeconds = 600;

    public static IReadOnlyList<OperatorNotifyEvent> DefaultEvents { get; } =
        Array.AsReadOnly(new[] { OperatorNotifyEvent.Approval, OperatorNotifyEvent.Question });

    public static OperatorNotifySettings Off { get; } = new(false, [], null, DefaultDelaySeconds, DefaultTimeoutSeconds);

    public OperatorNotifySettings(
        bool enabled,
        IReadOnlyList<string> command,
        IReadOnlyList<OperatorNotifyEvent>? events,
        int delaySeconds,
        int timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Operator notification command entries cannot be empty.", nameof(command));
        }

        if (enabled && command.Count == 0)
        {
            throw new ArgumentException("Operator notification requires a command when enabled.", nameof(command));
        }

        if (delaySeconds is < 0 or > MaximumDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delaySeconds),
                delaySeconds,
                $"Operator notification delay must be between 0 and {MaximumDelaySeconds} seconds.");
        }

        if (timeoutSeconds is < 1 or > MaximumTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds),
                timeoutSeconds,
                $"Operator notification timeout must be between 1 and {MaximumTimeoutSeconds} seconds.");
        }

        if (events is not null && events.Contains(OperatorNotifyEvent.Test))
        {
            throw new ArgumentException("The test event cannot be subscribed.", nameof(events));
        }

        Enabled = enabled;
        Command = Array.AsReadOnly(command.ToArray());
        Events = events is null
            ? DefaultEvents
            : Array.AsReadOnly(events.Distinct().ToArray());
        DelaySeconds = delaySeconds;
        TimeoutSeconds = timeoutSeconds;
    }

    public bool Enabled { get; }

    /// <summary>Executable followed by its arguments. No shell is involved.</summary>
    public IReadOnlyList<string> Command { get; }

    public IReadOnlyList<OperatorNotifyEvent> Events { get; }

    /// <summary>Seconds the operator may take to respond before the command runs.</summary>
    public int DelaySeconds { get; }

    public int TimeoutSeconds { get; }

    public bool HasCommand => Command.Count > 0;

    public bool HasDefaultEvents => Events.SequenceEqual(DefaultEvents);

    public bool IsDefault =>
        !Enabled
        && !HasCommand
        && HasDefaultEvents
        && DelaySeconds == DefaultDelaySeconds
        && TimeoutSeconds == DefaultTimeoutSeconds;

    public bool Sends(OperatorNotifyEvent value) => Enabled && Events.Contains(value);

    public OperatorNotifySettings WithEnabled(bool enabled) =>
        new(enabled, Command, Events, DelaySeconds, TimeoutSeconds);
}
