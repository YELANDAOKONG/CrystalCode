using CrystalCode.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Live workspace, session, and approval values for one session.
/// Exec children read them as environment variables. A dotnet host tool
/// receives a <see cref="ToolHostContext"/> captured from the same values.
/// </summary>
public sealed class SessionToolHost
{
    private readonly Workspace _workspace;
    private readonly Func<string> _sessionId;
    private readonly Func<string> _approval;

    public SessionToolHost(
        Workspace workspace,
        Func<string> sessionId,
        Func<string> approval)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(approval);
        _workspace = workspace;
        _sessionId = sessionId;
        _approval = approval;
    }

    public string WorkspaceRoot => _workspace.Root;

    public string SessionId => _sessionId() ?? string.Empty;

    public string Approval => _approval() ?? string.Empty;

    /// <summary>
    /// Captures the current workspace, session, and approval for one tool call.
    /// </summary>
    public ToolHostContext CreateContext() =>
        new(WorkspaceRoot, SessionId, Approval);
}
