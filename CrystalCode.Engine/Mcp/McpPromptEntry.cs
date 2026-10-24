using ModelContextProtocol.Client;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One prompt a connected server offers, reachable as a slash command.
/// </summary>
/// <param name="Command">The slash command name without the leading slash.</param>
/// <param name="Arguments">Argument names in the order the command takes them.</param>
internal sealed record McpPromptEntry(
    string Command,
    string ServerName,
    string RemoteName,
    string Description,
    IReadOnlyList<McpPromptArgument> Arguments,
    McpClient Client,
    int? TimeoutSeconds);
