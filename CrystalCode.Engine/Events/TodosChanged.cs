using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.Todos;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The session todo list changed.
/// </summary>
public sealed record TodosChanged(IReadOnlyList<TodoItem> Items) : SessionEvent;
