namespace CrystalCode.Engine.Tools.Todos;

/// <summary>
/// One session todo item.
/// </summary>
public sealed record TodoItem(string Id, string Content, TodoStatus Status);
