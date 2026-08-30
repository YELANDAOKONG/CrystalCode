namespace CrystalHarness.Home;

/// <summary>
/// One persisted session todo.
/// </summary>
public sealed class SessionTodoDocument
{
    public string? Id { get; set; }

    public string? Content { get; set; }

    public string? Status { get; set; }
}
