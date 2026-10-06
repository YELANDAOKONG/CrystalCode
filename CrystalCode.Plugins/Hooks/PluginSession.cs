namespace CrystalCode.Plugins.Hooks;

/// <summary>Read-only facts for session start and end.</summary>
public sealed record PluginSession
{
    public PluginSession(string workspaceRoot, string sessionId, string approvalMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalMode);
        WorkspaceRoot = workspaceRoot;
        SessionId = sessionId;
        ApprovalMode = approvalMode.Trim();
    }

    public string WorkspaceRoot { get; }

    public string SessionId { get; }

    public string ApprovalMode { get; }

    public override string ToString() => nameof(PluginSession);
}
