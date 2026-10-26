using System.Text.Json;
using System.Text.Json.Nodes;

using CrystalCode.Engine.Catalog;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Lists configured MCP servers and turns one on or off. Unlike a tool set,
/// a server may live in a shared <c>mcp.json</c>, so a change rewrites only
/// that server's entry and keeps every other entry and unknown field.
/// </summary>
public static class McpInventory
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static IReadOnlyList<McpServerEntry> List(CrystalHome home, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var discovery = new McpServerDiscovery(home);
        var notes = new List<string>();
        var homeEntries = discovery.Scope(McpServerSource.Home, workspaceRoot, notes);
        var projectEntries = discovery.Scope(McpServerSource.Project, workspaceRoot, notes);
        var replaced = projectEntries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. homeEntries.Select(entry => Convert(entry, McpServerSource.Home, !replaced.Contains(entry.Name))),
            .. projectEntries.Select(entry => Convert(entry, McpServerSource.Project, effective: true))
        ];
    }

    public static bool TryFind(
        CrystalHome home,
        string workspaceRoot,
        string name,
        McpServerSource? source,
        out McpServerEntry? entry,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        entry = null;
        if (!TryChoose(List(home, workspaceRoot), name, source, out entry, out error))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sets <c>enabled</c> on one server. <paramref name="changed"/> is false
    /// when the server already had that value.
    /// </summary>
    public static bool TrySetEnabled(
        CrystalHome home,
        string workspaceRoot,
        string name,
        McpServerSource? source,
        bool enabled,
        out McpServerEntry? entry,
        out bool changed,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        changed = false;
        if (!TryFind(home, workspaceRoot, name, source, out entry, out error) || entry is null)
        {
            return false;
        }

        if (entry.Transport is null)
        {
            error = $"MCP server '{entry.Name}' is invalid: {entry.Error}";
            return false;
        }

        if (entry.Enabled == enabled)
        {
            return true;
        }

        if (!TryWrite(entry, enabled, out error))
        {
            return false;
        }

        changed = true;
        return TryFind(home, workspaceRoot, entry.Name, entry.Source, out entry, out error);
    }

    public static IReadOnlyList<string> Format(IReadOnlyList<McpServerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return ["No MCP servers."];
        }

        IReadOnlyList<string> headers = ["Server", "Source", "Enabled", "Effective", "Transport", "Catalogs", "Path"];
        return AlignedRows.Format(headers, [.. entries.Select(Row)]);
    }

    public static IReadOnlyList<string> Format(McpServerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var fields = new List<(string Label, string Value)>
        {
            ("Server", entry.Name),
            ("Source", entry.Source.ToString()),
            ("Enabled", Flag(entry.Enabled)),
            ("Effective", entry.Effective ? "Yes" : "No"),
            ("Transport", Transport(entry.Transport)),
            ("Catalogs", entry.Catalogs.Length == 0 ? "-" : entry.Catalogs),
            ("Path", entry.Path)
        };
        if (entry.Error.Length > 0)
        {
            fields.Add(("Error", entry.Error));
        }

        var width = fields.Max(field => field.Label.Length);
        return [.. fields.Select(field => $"{field.Label.PadRight(width)}  {field.Value}")];
    }

    public static string Transport(McpTransportKind? transport) =>
        transport switch
        {
            McpTransportKind.Stdio => "stdio",
            McpTransportKind.StreamableHttp => "http",
            McpTransportKind.Sse => "sse",
            _ => "-"
        };

    private static IReadOnlyList<string> Row(McpServerEntry entry) =>
    [
        entry.Name,
        entry.Source.ToString(),
        Flag(entry.Enabled),
        entry.Effective ? "Yes" : "No",
        Transport(entry.Transport),
        entry.Catalogs.Length == 0 ? "-" : entry.Catalogs,
        entry.Path
    ];

    private static string Flag(bool? value) =>
        value switch
        {
            true => "Yes",
            false => "No",
            _ => "-"
        };

    private static McpServerEntry Convert(McpConfigEntry entry, McpServerSource source, bool effective)
    {
        var definition = entry.Definition;
        return new McpServerEntry(
            entry.Name,
            source,
            entry.Origin,
            effective,
            definition?.Transport,
            definition?.Enabled,
            definition is null
                ? string.Empty
                : string.Join(", ", new[] { definition.InPlan ? "plan" : null, definition.InWork ? "work" : null }.OfType<string>()),
            definition is null ? entry.Error : string.Empty);
    }

    private static bool TryChoose(
        IReadOnlyList<McpServerEntry> entries,
        string name,
        McpServerSource? source,
        out McpServerEntry? entry,
        out string error)
    {
        entry = null;
        error = string.Empty;
        var wanted = name.Trim();
        var matches = entries.Where(item => string.Equals(item.Name, wanted, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0 || (source is { } only && matches.All(item => item.Source != only)))
        {
            error = source is { } missing
                ? $"MCP server '{wanted}' was not found in {missing}."
                : $"MCP server '{wanted}' was not found.";
            return false;
        }

        // The Project definition is the one that takes effect, so it is the default.
        entry = source is { } chosen
            ? matches.First(item => item.Source == chosen)
            : matches.FirstOrDefault(item => item.Effective) ?? matches[0];
        return true;
    }

    private static bool TryWrite(McpServerEntry entry, bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            var text = File.ReadAllText(entry.Path);
            if (JsonNode.Parse(text) is not JsonObject root)
            {
                error = "The MCP file must be a JSON object.";
                return false;
            }

            // A server.json is one definition. mcp.json nests it under mcpServers.
            var target = System.IO.Path.GetFileName(entry.Path).Equals(McpFiles.ServerFileName, StringComparison.Ordinal)
                ? root
                : (root["mcpServers"] as JsonObject)?[entry.Name] as JsonObject;
            if (target is null)
            {
                error = $"MCP server '{entry.Name}' was not found in '{entry.Path}'.";
                return false;
            }

            target["enabled"] = JsonValue.Create(enabled);
            // mcp.json may carry headers or environment values; keep its mode.
            AtomicFile.WriteText(entry.Path, root.ToJsonString(Indented) + Environment.NewLine, ownerOnly: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "The MCP file could not be updated.";
            return false;
        }
    }
}
