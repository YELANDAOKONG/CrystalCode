using CrystalCode.Engine.Home;
using CrystalCode.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Live workspace, session, approval, and data-directory values for one
/// session. Exec children read the workspace, session, and approval values as
/// environment variables. A dotnet host tool receives a
/// <see cref="ToolHostContext"/> captured from the same values.
/// </summary>
public sealed class SessionToolHost
{
    private readonly Workspace _workspace;
    private readonly CrystalHome _home;
    private readonly Func<string> _sessionId;
    private readonly Func<string> _approval;

    public SessionToolHost(
        Workspace workspace,
        CrystalHome home,
        Func<string> sessionId,
        Func<string> approval)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(approval);
        _workspace = workspace;
        _home = home;
        _sessionId = sessionId;
        _approval = approval;
    }

    public string WorkspaceRoot => _workspace.Root;

    public string SessionId => _sessionId() ?? string.Empty;

    public string Approval => _approval() ?? string.Empty;

    /// <summary>
    /// Resolves the global and project data directories for one extension of
    /// the current workspace. Nothing is created.
    /// </summary>
    public ExtensionDataPaths ExtensionData(ExtensionDataKind kind, string directoryName) =>
        ExtensionDataPaths.Resolve(_home, WorkspaceRoot, kind, directoryName);

    /// <summary>
    /// Captures the current workspace, session, approval, and the resolved data
    /// directories for one extension, creating those directories lazily.
    /// </summary>
    public ToolHostContext CreateContext(ExtensionDataKind kind, string directoryName)
    {
        var paths = ExtensionData(kind, directoryName);
        _ = paths.EnsureCreated();
        return new ToolHostContext(
            WorkspaceRoot,
            SessionId,
            Approval,
            paths.GlobalDirectory,
            paths.ProjectDirectory);
    }
}
