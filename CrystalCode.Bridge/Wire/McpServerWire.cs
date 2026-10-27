using System.Text.Json;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Mcp;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// Reads ACP <c>mcpServers</c> into session MCP definitions
/// (<see cref="McpServerSource.Session"/>). Stdio is always accepted; http
/// and sse are accepted because Bridge advertises them. Values can hold
/// secrets, so no error names a command, argument, URL, header, or
/// environment value; an error names the server and the field only.
/// </summary>
internal static class McpServerWire
{
    public static IReadOnlyList<McpServerDefinition> Read(JsonElement? servers)
    {
        if (servers is not { } list)
        {
            return [];
        }

        var definitions = new List<McpServerDefinition>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in list.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object)
            {
                throw JsonRpcException.InvalidParams("Each MCP server must be an object.");
            }

            var name = JsonParams.RequiredString(server, "name");
            if (!McpServerNames.IsServerName(name))
            {
                throw JsonRpcException.InvalidParams(
                    "MCP server names start with a letter and use letters, digits, hyphens, and single underscores.");
            }

            if (!names.Add(name))
            {
                throw JsonRpcException.InvalidParams("MCP server '" + name + "' is listed more than once.");
            }

            definitions.Add(Definition(server, name));
        }

        return definitions;
    }

    private static McpServerDefinition Definition(JsonElement server, string name)
    {
        var type = JsonParams.OptionalString(server, "type");
        try
        {
            switch (type)
            {
                case null or "stdio":
                    var command = Field(server, name, "command");
                    if (!Path.IsPathFullyQualified(command))
                    {
                        throw JsonRpcException.InvalidParams("MCP server '" + name + "' needs an absolute command path.");
                    }

                    return new McpServerDefinition(
                        name,
                        McpServerSource.Session,
                        McpTransportKind.Stdio,
                        command: command,
                        arguments: Strings(server, name, "args"),
                        environment: Pairs(server, name, "env"));
                case "http" or "sse":
                    return new McpServerDefinition(
                        name,
                        McpServerSource.Session,
                        type == "sse" ? McpTransportKind.Sse : McpTransportKind.StreamableHttp,
                        url: Field(server, name, "url"),
                        headers: Pairs(server, name, "headers"));
                default:
                    throw JsonRpcException.InvalidParams("MCP server '" + name + "' has an unsupported type.");
            }
        }
        catch (ArgumentException)
        {
            throw JsonRpcException.InvalidParams("MCP server '" + name + "' is not a valid definition.");
        }
    }

    private static string Field(JsonElement server, string name, string field) =>
        JsonParams.OptionalString(server, field) is { Length: > 0 } value
            ? value
            : throw JsonRpcException.InvalidParams("MCP server '" + name + "' needs " + field + ".");

    private static IReadOnlyList<string> Strings(JsonElement server, string name, string field)
    {
        if (JsonParams.OptionalArray(server, field) is not { } array)
        {
            return [];
        }

        return [.. array.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw JsonRpcException.InvalidParams("MCP server '" + name + "' has a non-string " + field + " entry."))];
    }

    private static IReadOnlyDictionary<string, string> Pairs(JsonElement server, string name, string field)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (JsonParams.OptionalArray(server, field) is not { } array)
        {
            return pairs;
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || JsonParams.OptionalString(entry, "name") is not { Length: > 0 } key
                || JsonParams.OptionalString(entry, "value") is not { } value)
            {
                throw JsonRpcException.InvalidParams("MCP server '" + name + "' has an invalid " + field + " entry.");
            }

            pairs[key] = value;
        }

        return pairs;
    }
}
