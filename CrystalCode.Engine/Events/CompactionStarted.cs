namespace CrystalCode.Engine.Events;

/// <summary>
/// A context compaction began. <see cref="Id"/> is unique within the session
/// and is repeated by the matching <see cref="CompactionFinished"/>.
/// <see cref="Manual"/> is true for <c>/compact</c>.
/// </summary>
public sealed record CompactionStarted(string Id, bool Manual) : SessionEvent;
