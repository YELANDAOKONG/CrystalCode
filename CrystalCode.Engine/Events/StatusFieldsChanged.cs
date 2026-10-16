namespace CrystalCode.Engine.Events;

/// <summary>
/// The latest plugin status-field values. Names that are absent were cleared.
/// </summary>
public sealed record StatusFieldsChanged(IReadOnlyList<StatusFieldValue> Fields) : SessionEvent;
