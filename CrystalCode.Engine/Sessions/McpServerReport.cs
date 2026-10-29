namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One configured or connected MCP server. It carries no command, argument,
/// environment, header, or URL, because those can hold secrets.
/// <see cref="Status"/> is the connection state (<c>connected</c>,
/// <c>connecting</c>, <c>failed</c>, <c>disconnected</c>), or <c>disabled</c>,
/// <c>replaced</c> (a later source of the same name takes effect), or
/// <c>invalid</c> for a server that is not connected for that reason.
/// <see cref="Transport"/> is empty for an invalid definition.
/// <see cref="Error"/> is empty unless the connection or definition failed.
/// </summary>
public sealed record McpServerReport(
    string Name,
    string Source,
    string Transport,
    string Status,
    IReadOnlyList<string> Tools,
    string Error,
    bool Enabled = true,
    bool Effective = true);
