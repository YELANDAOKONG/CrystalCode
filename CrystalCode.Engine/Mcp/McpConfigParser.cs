using System.Text.Json;

using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Reads <c>mcp.json</c> and <c>server.json</c> content. Parsing is strict:
/// unknown or duplicate fields skip the server instead of guessing.
/// </summary>
internal static class McpConfigParser
{
    private const string ServersProperty = "mcpServers";

    private static readonly HashSet<string> ServerFields = new(StringComparer.Ordinal)
    {
        "type",
        "command",
        "args",
        "env",
        "url",
        "headers",
        "catalogs",
        "toolTimeoutSeconds"
    };

    public static IReadOnlyList<McpConfigEntry> ParseFile(
        string json,
        McpServerSource source,
        string origin,
        IList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(notes);
        if (!TryOpen(json, origin, notes, out var document))
        {
            return [];
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                notes.Add($"MCP file '{origin}' was skipped: the top level must be an object.");
                return [];
            }

            var found = false;
            var servers = default(JsonElement);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != ServersProperty)
                {
                    notes.Add($"MCP file '{origin}' was skipped: unknown field '{property.Name}'.");
                    return [];
                }

                if (found)
                {
                    notes.Add($"MCP file '{origin}' was skipped: duplicate field '{ServersProperty}'.");
                    return [];
                }

                found = true;
                servers = property.Value;
            }

            if (!found)
            {
                return [];
            }

            if (servers.ValueKind != JsonValueKind.Object)
            {
                notes.Add($"MCP file '{origin}' was skipped: '{ServersProperty}' must be an object.");
                return [];
            }

            return ParseServers(servers, source, origin, notes);
        }
    }

    public static McpConfigEntry ParseSingle(
        string json,
        string name,
        McpServerSource source,
        string origin,
        IList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(notes);
        if (!TryOpen(json, origin, notes, out var document))
        {
            return new McpConfigEntry(name, null, origin);
        }

        using (document)
        {
            return ParseEntry(name, document.RootElement, source, origin, notes);
        }
    }

    private static List<McpConfigEntry> ParseServers(
        JsonElement servers,
        McpServerSource source,
        string origin,
        IList<string> notes)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in servers.EnumerateObject())
        {
            counts[property.Name] = counts.GetValueOrDefault(property.Name) + 1;
        }

        var entries = new List<McpConfigEntry>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in servers.EnumerateObject())
        {
            if (counts[property.Name] > 1)
            {
                if (reported.Add(property.Name))
                {
                    notes.Add(
                        $"MCP server '{property.Name}' ({origin}) was skipped: "
                        + "the name appears more than once.");
                    entries.Add(new McpConfigEntry(property.Name, null, origin));
                }

                continue;
            }

            entries.Add(ParseEntry(property.Name, property.Value, source, origin, notes));
        }

        return entries;
    }

    private static McpConfigEntry ParseEntry(
        string name,
        JsonElement element,
        McpServerSource source,
        string origin,
        IList<string> notes)
    {
        var definition = ParseServer(name, element, source, out var error);
        if (definition is null)
        {
            notes.Add($"MCP server '{name}' ({origin}) was skipped: {error}");
        }

        return new McpConfigEntry(name, definition, origin);
    }

    private static bool TryOpen(
        string json,
        string origin,
        IList<string> notes,
        out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                    MaxDepth = 16
                });
            return true;
        }
        catch (JsonException)
        {
            notes.Add($"MCP file '{origin}' was skipped: it is not valid JSON.");
            document = null!;
            return false;
        }
    }

    private static McpServerDefinition? ParseServer(
        string name,
        JsonElement element,
        McpServerSource source,
        out string error)
    {
        error = string.Empty;
        if (!McpServerNames.IsServerName(name))
        {
            error = "the name is invalid. Use letters, digits, '-' and single '_', "
                + $"start with a letter, and stay within {McpServerNames.MaximumServerNameLength} characters.";
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "the definition must be an object.";
            return null;
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!ServerFields.Contains(property.Name))
            {
                error = $"unknown field '{property.Name}'.";
                return null;
            }

            if (!fields.TryAdd(property.Name, property.Value))
            {
                error = $"duplicate field '{property.Name}'.";
                return null;
            }
        }

        if (!TryString(fields, "type", out var type, out error)
            || !TryString(fields, "command", out var command, out error)
            || !TryString(fields, "url", out var url, out error)
            || !TryStringArray(fields, "args", out var arguments, out error)
            || !TryStringMap(fields, "env", StringComparer.Ordinal, out var environment, out error)
            || !TryStringMap(fields, "headers", StringComparer.OrdinalIgnoreCase, out var headers, out error)
            || !TryCatalogs(fields, out var inPlan, out var inWork, out error)
            || !TryTimeout(fields, out var timeout, out error)
            || !TryTransport(fields, type, out var transport, out error))
        {
            return null;
        }

        return new McpServerDefinition(
            name,
            source,
            transport,
            command,
            arguments,
            environment,
            url,
            headers,
            inPlan,
            inWork,
            timeout);
    }

    private static bool TryTransport(
        Dictionary<string, JsonElement> fields,
        string? type,
        out McpTransportKind transport,
        out string error)
    {
        error = string.Empty;
        transport = McpTransportKind.Stdio;
        var hasCommand = fields.ContainsKey("command");
        var hasUrl = fields.ContainsKey("url");
        if (type is null)
        {
            if (hasCommand == hasUrl)
            {
                error = "it needs exactly one of 'command' or 'url'.";
                return false;
            }

            transport = hasCommand ? McpTransportKind.Stdio : McpTransportKind.StreamableHttp;
        }
        else
        {
            switch (type.ToLowerInvariant())
            {
                case "stdio":
                    transport = McpTransportKind.Stdio;
                    break;
                case "http":
                    transport = McpTransportKind.StreamableHttp;
                    break;
                case "sse":
                    transport = McpTransportKind.Sse;
                    break;
                default:
                    error = $"type '{type}' is not supported. Use stdio, http, or sse.";
                    return false;
            }
        }

        if (transport == McpTransportKind.Stdio)
        {
            if (!hasCommand)
            {
                error = "a stdio server needs 'command'.";
                return false;
            }

            if (hasUrl || fields.ContainsKey("headers"))
            {
                error = "a stdio server cannot have 'url' or 'headers'.";
                return false;
            }

            return true;
        }

        if (!hasUrl)
        {
            error = "an HTTP server needs 'url'.";
            return false;
        }

        if (hasCommand || fields.ContainsKey("args") || fields.ContainsKey("env"))
        {
            error = "an HTTP server cannot have 'command', 'args', or 'env'.";
            return false;
        }

        return true;
    }

    private static bool TryString(
        Dictionary<string, JsonElement> fields,
        string field,
        out string? value,
        out string error)
    {
        error = string.Empty;
        value = null;
        if (!fields.TryGetValue(field, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(element.GetString()))
        {
            value = element.GetString()!.Trim();
            return true;
        }

        error = $"'{field}' must be a non-empty string.";
        return false;
    }

    private static bool TryStringArray(
        Dictionary<string, JsonElement> fields,
        string field,
        out IReadOnlyList<string>? values,
        out string error)
    {
        error = string.Empty;
        values = null;
        if (!fields.TryGetValue(field, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"'{field}' must be an array of strings.";
            return false;
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"'{field}' must be an array of strings.";
                return false;
            }

            list.Add(item.GetString()!);
        }

        values = list;
        return true;
    }

    private static bool TryStringMap(
        Dictionary<string, JsonElement> fields,
        string field,
        StringComparer comparer,
        out IReadOnlyDictionary<string, string>? values,
        out string error)
    {
        error = string.Empty;
        values = null;
        if (!fields.TryGetValue(field, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"'{field}' must be an object of strings.";
            return false;
        }

        var map = new Dictionary<string, string>(comparer);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.Name)
                || property.Name.Contains('='))
            {
                error = $"'{field}' must be an object of strings with plain names.";
                return false;
            }

            if (!map.TryAdd(property.Name, property.Value.GetString()!))
            {
                error = $"'{field}' repeats the name '{property.Name}'.";
                return false;
            }
        }

        values = map;
        return true;
    }

    private static bool TryCatalogs(
        Dictionary<string, JsonElement> fields,
        out bool inPlan,
        out bool inWork,
        out string error)
    {
        error = string.Empty;
        inPlan = false;
        inWork = true;
        if (!fields.TryGetValue("catalogs", out var element))
        {
            return true;
        }

        var plan = false;
        var work = false;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var text = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : null;
                if (text == "plan" && !plan)
                {
                    plan = true;
                }
                else if (text == "work" && !work)
                {
                    work = true;
                }
                else
                {
                    plan = false;
                    work = false;
                    break;
                }
            }
        }

        if (!plan && !work)
        {
            error = "'catalogs' must list \"plan\", \"work\", or both, without repeats.";
            return false;
        }

        inPlan = plan;
        inWork = work;
        return true;
    }

    private static bool TryTimeout(
        Dictionary<string, JsonElement> fields,
        out int? seconds,
        out string error)
    {
        error = string.Empty;
        seconds = McpServerDefinition.DefaultToolTimeoutSeconds;
        if (!fields.TryGetValue("toolTimeoutSeconds", out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Null
            || element.ValueKind == JsonValueKind.String && element.GetString() == "unlimited")
        {
            seconds = null;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var parsed)
            && BashTool.IsSupported(parsed))
        {
            seconds = parsed;
            return true;
        }

        error = "'toolTimeoutSeconds' must be a positive number of seconds or \"unlimited\".";
        return false;
    }
}
