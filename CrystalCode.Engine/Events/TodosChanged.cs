using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The session todo list changed.
/// </summary>
public sealed record TodosChanged(IReadOnlyList<TodoItem> Items) : SessionEvent;
