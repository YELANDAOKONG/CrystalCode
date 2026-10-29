namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One connected or configured MCP server. It carries no command, argument,
/// environment, header, or URL, because those can hold secrets.
/// <see cref="Error"/> is empty unless the connection failed.
/// </summary>
public sealed record McpServerReport(
    string Name,
    string Source,
    string Transport,
    string Status,
    IReadOnlyList<string> Tools,
    string Error);
