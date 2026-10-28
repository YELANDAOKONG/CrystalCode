namespace CrystalCode.Engine.Events;

/// <summary>
/// The engine started loading one operator tool set or plugin, or started
/// connecting one MCP server. <paramref name="Name"/> is the tool set or
/// plugin directory name, or the MCP server name. MCP servers connect at the
/// same time, so several of them start together. It is published only while
/// <see cref="ActivityChanged"/> reports a loading activity.
/// </summary>
public sealed record LoadingProgressed(LoadingKind Kind, string Name) : SessionEvent;
