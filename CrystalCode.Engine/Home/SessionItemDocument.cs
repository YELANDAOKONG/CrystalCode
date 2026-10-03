namespace CrystalCode.Engine.Home;

/// <summary>
/// One persisted transcript item.
/// </summary>
public sealed class SessionItemDocument
{
    public string? Kind { get; set; }

    public string? Role { get; set; }

    public string? Text { get; set; }

    public string? CallId { get; set; }

    public string? Name { get; set; }

    public string? Arguments { get; set; }

    public string? Status { get; set; }

    public List<SessionReasoningSegment>? Segments { get; set; }

    public string? StateFormat { get; set; }

    public string? StateData { get; set; }
}
