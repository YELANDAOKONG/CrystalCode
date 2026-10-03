namespace CrystalCode.Engine.Home;

/// <summary>
/// One readable segment of a persisted reasoning item.
/// </summary>
public sealed class SessionReasoningSegment
{
    public string? Text { get; set; }

    public string? Kind { get; set; }
}
