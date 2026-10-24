namespace CrystalCode.Engine.Mcp;

/// <summary>
/// What a front end shows about one MCP tool. <paramref name="ReadOnly"/> is
/// true only when the server declared the tool read-only and the operator's
/// approval policy trusts that declaration.
/// </summary>
public sealed record McpToolInfo(
    string ModelName,
    string ServerName,
    string RemoteName,
    bool ReadOnly,
    bool InPlan,
    bool InWork);
