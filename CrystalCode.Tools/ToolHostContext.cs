namespace CrystalCode.Tools;

/// <summary>
/// Host facts for one tool call. The host builds a new value when the call
/// starts. A tool that keeps the instance does not observe later session changes.
/// </summary>
public sealed record ToolHostContext
{
    /// <summary>
    /// Initializes host facts for one call.
    /// </summary>
    /// <param name="workspaceRoot">The session workspace root.</param>
    /// <param name="sessionId">The current session id, or empty when none is assigned.</param>
    /// <param name="approval">
    /// The current approval mode, or empty when the host has none.
    /// Known modes are plan, default, edit, review, audit, and full.
    /// </param>
    /// <param name="globalDataDirectory">
    /// This extension's global runtime-data directory, or empty when the host
    /// has none. It is shared across workspaces and lives under the data
    /// directory.
    /// </param>
    /// <param name="projectDataDirectory">
    /// This extension's project runtime-data directory, or empty when the host
    /// has none. It belongs to the current workspace.
    /// </param>
    public ToolHostContext(
        string workspaceRoot,
        string sessionId,
        string approval,
        string globalDataDirectory = "",
        string projectDataDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(globalDataDirectory);
        ArgumentNullException.ThrowIfNull(projectDataDirectory);
        WorkspaceRoot = workspaceRoot;
        SessionId = sessionId;
        Approval = approval;
        GlobalDataDirectory = globalDataDirectory;
        ProjectDataDirectory = projectDataDirectory;
    }

    /// <summary>Gets the session workspace root for this call.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>Gets the session id for this call.</summary>
    public string SessionId { get; }

    /// <summary>Gets the approval mode for this call.</summary>
    public string Approval { get; }

    /// <summary>Gets this extension's global runtime-data directory, or empty.</summary>
    public string GlobalDataDirectory { get; }

    /// <summary>Gets this extension's project runtime-data directory, or empty.</summary>
    public string ProjectDataDirectory { get; }
}
