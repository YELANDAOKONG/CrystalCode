namespace CrystalHarness.Home;

/// <summary>
/// JSON shape for <c>~/.crystal/sessions/&lt;id&gt;.json</c>.
/// </summary>
public sealed class SessionDocument
{
    public string? Id { get; set; }

    public string? Workspace { get; set; }

    public bool PlanMode { get; set; }

    public DateTimeOffset? CreatedUtc { get; set; }

    public DateTimeOffset? UpdatedUtc { get; set; }

    public List<SessionItemDocument> Items { get; set; } = [];

    public List<SessionTodoDocument> Todos { get; set; } = [];
}
