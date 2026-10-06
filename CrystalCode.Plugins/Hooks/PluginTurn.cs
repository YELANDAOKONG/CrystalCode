namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// One user turn. <see cref="StopReason"/> and <see cref="Error"/> are set
/// when the turn ends and are null when it starts.
/// </summary>
public sealed record PluginTurn
{
    public PluginTurn(string sessionId, string mode, string userText)
        : this(sessionId, mode, userText, null, null)
    {
    }

    public PluginTurn(
        string sessionId,
        string mode,
        string userText,
        string? stopReason,
        string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(userText);
        SessionId = sessionId.Trim();
        Mode = mode.Trim();
        UserText = userText;
        StopReason = string.IsNullOrWhiteSpace(stopReason) ? null : stopReason.Trim();
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    public string SessionId { get; }

    public string Mode { get; }

    public string UserText { get; }

    public string? StopReason { get; }

    public string? Error { get; }

    public override string ToString() => nameof(PluginTurn);
}
