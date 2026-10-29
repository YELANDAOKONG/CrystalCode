namespace CrystalCode.Engine.Events;

/// <summary>
/// Session settings chosen while a turn or compaction ran. They apply when
/// that work is collected, before a queued follow-up starts. A null field has
/// no pending value. <see cref="ServiceTier"/> and <see cref="Speed"/> use
/// <see cref="Unset"/> for a pending change to no token.
/// </summary>
public sealed record SessionPendingSettings(
    string? Provider = null,
    string? Model = null,
    int? ContextWindow = null,
    string? ServiceTier = null,
    string? Speed = null,
    string? Thinking = null,
    string? Mode = null,
    string? Approval = null)
{
    public const string Unset = "unset";

    public static SessionPendingSettings None { get; } = new();

    public bool IsEmpty => this == None;
}
