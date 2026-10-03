namespace CrystalCode.Engine.Events;

/// <summary>
/// The in-memory side-question thread. <see cref="Announce"/> asks the front
/// end to show the panel. Later updates with the same question keep the
/// panel closed when the operator has already dismissed it.
/// </summary>
public sealed record SideQuestionSnapshot(
    bool Announce,
    IReadOnlyList<SideExchange> Exchanges,
    bool Running,
    string PendingQuestion,
    string LiveAnswer,
    string? Failure) : SessionEvent
{
    public static SideQuestionSnapshot Empty { get; } = new(true, [], false, string.Empty, string.Empty, null);
}
