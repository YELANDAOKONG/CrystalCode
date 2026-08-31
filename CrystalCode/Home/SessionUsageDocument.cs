namespace CrystalCode.Home;

/// <summary>
/// Last provider-reported token usage persisted with a session.
/// </summary>
public sealed class SessionUsageDocument
{
    public long InputTokenCount { get; set; }

    public long OutputTokenCount { get; set; }

    public long? ReasoningTokenCount { get; set; }
}
