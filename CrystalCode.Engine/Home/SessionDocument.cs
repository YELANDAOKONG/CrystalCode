using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

/// <summary>
/// JSON shape for <c>~/.crystal/sessions/&lt;id&gt;.json</c>.
/// </summary>
public sealed class SessionDocument
{
    public string? Id { get; set; }

    public string? Workspace { get; set; }

    public bool PlanMode { get; set; }

    /// <summary>
    /// Custom mode name. Null means Plan or Work, chosen by <see cref="PlanMode"/>.
    /// </summary>
    public string? Mode { get; set; }

    public IReadOnlyList<DirectoryAccess> Access { get; set; } = [];

    /// <summary>
    /// Built-in tools disabled for this session only. Null means none.
    /// </summary>
    public BuiltInToolDisablementDocument? DisabledBuiltInTools { get; set; }

    public DateTimeOffset? CreatedUtc { get; set; }

    public DateTimeOffset? UpdatedUtc { get; set; }

    public List<SessionItemDocument> Items { get; set; } = [];

    /// <summary>
    /// Operator history. Null means a session saved before the archive existed;
    /// the host then copies <see cref="Items"/>.
    /// </summary>
    public List<SessionItemDocument>? Archive { get; set; }

    public List<SessionImageDocument> Images { get; set; } = [];

    public List<ChildSessionDocument> Children { get; set; } = [];

    public bool ImageMarkersTagged { get; set; }

    public List<SessionTodoDocument> Todos { get; set; } = [];

    public int UserTurns { get; set; }

    public int ModelCalls { get; set; }

    public int ToolCalls { get; set; }

    public SessionUsageDocument? Usage { get; set; }

    public SessionUsageDocument? CumulativeUsage { get; set; }
}
