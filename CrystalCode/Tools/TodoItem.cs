namespace CrystalCode.Tools;

/// <summary>
/// One session todo item.
/// </summary>
public sealed record TodoItem(string Id, string Content, TodoStatus Status);
