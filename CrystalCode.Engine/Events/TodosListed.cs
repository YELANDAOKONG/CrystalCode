using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.Todos;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The full session todo list requested by <c>/todos</c>.
/// </summary>
public sealed record TodosListed(IReadOnlyList<TodoItem> Items) : SessionEvent;
