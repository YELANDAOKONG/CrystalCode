namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// What <c>session/new</c>, <c>session/load</c>, and <c>session/resume</c>
/// asked for, after validation.
/// </summary>
/// <param name="Cwd">The absolute working directory, or null for the command-line fallback.</param>
/// <param name="McpServers">Session MCP servers from the client.</param>
/// <param name="AdditionalRoots">The complete additional-root list; empty means none.</param>
/// <param name="ResumeId">The saved session to restore, or null for a new one.</param>
/// <param name="Replay">Stream the archive before the response, as <c>session/load</c> does.</param>
internal sealed record SessionSetup(
    string? Cwd,
    IReadOnlyList<Engine.Mcp.McpServerDefinition> McpServers,
    IReadOnlyList<string> AdditionalRoots,
    string? ResumeId = null,
    bool Replay = false);
