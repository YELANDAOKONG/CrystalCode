using System.Text.Json;

namespace CrystalCode.Tools.External;

/// <summary>
/// Reads one <c>tools.json</c> file into a <see cref="ParsedToolSet"/>.
/// </summary>
public static class ToolsManifestParser
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static bool TryParse(
        string directory,
        string json,
        out ParsedToolSet? set,
        out string error)
    {
        set = null;
        error = string.Empty;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(json);

        string directoryName;
        try
        {
            directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "Tool set directory is not a valid path.";
            return false;
        }

        if (!ExternalToolNames.IsDirectoryName(directoryName))
        {
            error = $"Tool set directory '{directoryName}' is not a valid name.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            error = "tools.json is not valid JSON: " + exception.Message;
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "tools.json must be a JSON object.";
                return false;
            }

            return TryRead(directory, directoryName, document.RootElement, out set, out error);
        }
    }

    private static bool TryRead(
        string directory,
        string directoryName,
        JsonElement root,
        out ParsedToolSet? set,
        out string error)
    {
        set = null;
        error = string.Empty;
        if (!TryReadString(root, "runner", out var runnerText)
            || !ExternalRunnerKind.TryParse(runnerText, out var runner))
        {
            error = "tools.json must set runner to exec or dotnet.";
            return false;
        }

        if (!TryReadOptionalStringList(root, "catalogs", out var catalogValues, out error)
            || !ExternalCatalogSelection.TryParse(catalogValues, out var catalogs))
        {
            error = "catalogs must be plan, work, or both.";
            return false;
        }

        var timeout = WorkspaceLimits.BashTimeoutSeconds;
        if (root.TryGetProperty("timeoutSeconds", out var timeoutElement))
        {
            if (timeoutElement.ValueKind != JsonValueKind.Number
                || !timeoutElement.TryGetInt32(out timeout)
                || timeout <= 0)
            {
                error = "timeoutSeconds must be a positive integer.";
                return false;
            }
        }

        if (!TryReadOptionalBoolean(root, "stdin", defaultValue: true, out var stdin, out error))
        {
            return false;
        }

        if (!TryReadOptionalBoolean(root, "enabled", defaultValue: true, out var enabled, out error))
        {
            return false;
        }

        if (!TryReadOptionalStringList(root, "command", out var commandValues, out error))
        {
            error = "command must be an array of strings.";
            return false;
        }

        var command = commandValues ?? [];
        var hasTools = root.TryGetProperty("tools", out var toolsElement)
            && toolsElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object;
        var hasShorthand = HasShorthandFields(root);

        if (runner == ExternalRunnerKind.Exec)
        {
            if (hasTools && toolsElement.ValueKind != JsonValueKind.Array)
            {
                error = "exec tools must be a JSON array.";
                return false;
            }

            if (hasTools && hasShorthand)
            {
                error = "exec shorthand fields cannot mix with a tools array.";
                return false;
            }

            IReadOnlyList<ExternalToolSpec> tools;
            if (hasTools)
            {
                if (!TryReadExecTools(toolsElement, catalogs, out tools, out error))
                {
                    return false;
                }
            }
            else
            {
                if (!TryReadShorthand(root, directoryName, catalogs, out var spec, out error))
                {
                    return false;
                }

                tools = [spec];
            }

            if (command.Count == 0 && tools.All(tool => tool.CommandSuffix.Count == 0))
            {
                error = "exec requires a command array.";
                return false;
            }

            set = new ParsedToolSet(
                directory,
                directoryName,
                runner,
                command,
                stdin,
                enabled,
                timeout,
                catalogs,
                tools);
            return true;
        }

        if (hasTools && toolsElement.ValueKind != JsonValueKind.Object)
        {
            error = "dotnet tools must be a JSON object of overlays.";
            return false;
        }

        if (!TryReadString(root, "assembly", out var assembly)
            || string.IsNullOrWhiteSpace(assembly))
        {
            error = "dotnet runner requires assembly.";
            return false;
        }

        if (!TryReadOptionalStringList(root, "types", out var typeValues, out error))
        {
            error = "types must be an array of strings.";
            return false;
        }

        var types = typeValues ?? [];
        var overlays = new List<ExternalToolSpec>();
        if (hasTools)
        {
            if (!TryReadDotnetOverlays(toolsElement, catalogs, out overlays, out error))
            {
                return false;
            }
        }

        set = new ParsedToolSet(
            directory,
            directoryName,
            runner,
            command,
            stdin,
            enabled,
            timeout,
            catalogs,
            overlays,
            assembly.Trim(),
            types);
        return true;
    }

    private static bool HasShorthandFields(JsonElement root) =>
        HasNonNull(root, "description")
        || HasSchema(root)
        || HasNonNull(root, "argv")
        || HasNonNull(root, "pathArguments");

    private static bool HasNonNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property)
        && property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

    private static bool HasSchema(JsonElement root) =>
        root.TryGetProperty("schema", out var schema)
        && schema.ValueKind == JsonValueKind.Object;

    private static bool TryReadShorthand(
        JsonElement root,
        string directoryName,
        ExternalCatalogSelection catalogs,
        out ExternalToolSpec spec,
        out string error)
    {
        spec = null!;
        if (!ExternalToolNames.IsToolName(directoryName))
        {
            error = $"Directory '{directoryName}' is not a valid tool name for exec shorthand.";
            return false;
        }

        return TryReadExecTool(
            root,
            catalogs,
            directoryName,
            allowMissingName: true,
            out spec,
            out error);
    }

    private static bool TryReadExecTools(
        JsonElement array,
        ExternalCatalogSelection defaults,
        out IReadOnlyList<ExternalToolSpec> tools,
        out string error)
    {
        tools = [];
        error = string.Empty;
        var list = new List<ExternalToolSpec>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                error = "Each exec tool must be a JSON object.";
                return false;
            }

            if (!TryReadExecTool(element, defaults, null, allowMissingName: false, out var spec, out error))
            {
                return false;
            }

            if (!names.Add(spec.Name))
            {
                error = $"Duplicate tool name '{spec.Name}'.";
                return false;
            }

            list.Add(spec);
        }

        if (list.Count == 0)
        {
            error = "exec tools array must not be empty.";
            return false;
        }

        tools = list;
        return true;
    }

    private static bool TryReadExecTool(
        JsonElement element,
        ExternalCatalogSelection defaults,
        string? fallbackName,
        bool allowMissingName,
        out ExternalToolSpec spec,
        out string error)
    {
        spec = null!;
        error = string.Empty;
        var name = fallbackName ?? string.Empty;
        if (!allowMissingName)
        {
            if (!TryReadString(element, "name", out var named) || named.Length == 0)
            {
                error = "Each exec tool requires name.";
                return false;
            }

            name = named;
        }
        else if (string.IsNullOrWhiteSpace(name))
        {
            error = "Each exec tool requires name.";
            return false;
        }

        if (!ExternalToolNames.IsToolName(name))
        {
            error = $"Tool name '{name}' is reserved or invalid.";
            return false;
        }

        if (!TryReadString(element, "description", out var description)
            || description.Length == 0)
        {
            error = $"Tool '{name}' requires description.";
            return false;
        }

        if (!element.TryGetProperty("schema", out var schema)
            || schema.ValueKind != JsonValueKind.Object)
        {
            error = $"Tool '{name}' requires a JSON Schema object.";
            return false;
        }

            var catalogs = defaults;
            if (!TryReadOptionalStringList(element, "catalogs", out var catalogValues, out error)
                || (catalogValues is not null
                    && !ExternalCatalogSelection.TryParse(catalogValues, out catalogs)))
            {
                error = $"Tool '{name}' has invalid catalogs.";
                return false;
            }

            if (!TryReadArgv(element, out var argv, out error))
            {
                return false;
            }

            if (!TryReadOptionalStringList(element, "command", out var suffix, out error))
            {
                error = $"Tool '{name}' command must be an array of strings.";
                return false;
            }

            if (!TryReadOptionalStringList(element, "pathArguments", out var pathArguments, out error))
            {
                error = $"Tool '{name}' pathArguments must be an array of strings.";
                return false;
            }

            spec = new ExternalToolSpec(
                name,
                description,
                schema.Clone(),
                catalogs,
                suffix,
                argv,
                pathArguments);
        return true;
    }

    private static bool TryReadDotnetOverlays(
        JsonElement map,
        ExternalCatalogSelection defaults,
        out List<ExternalToolSpec> overlays,
        out string error)
    {
        overlays = [];
        error = string.Empty;
        foreach (var property in map.EnumerateObject())
        {
            if (!ExternalToolNames.IsToolName(property.Name))
            {
                error = $"Overlay '{property.Name}' is reserved or invalid.";
                return false;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                error = $"Overlay '{property.Name}' must be a JSON object.";
                return false;
            }

            var catalogs = defaults;
            if (!TryReadOptionalStringList(property.Value, "catalogs", out var catalogValues, out error)
                || (catalogValues is not null
                    && !ExternalCatalogSelection.TryParse(catalogValues, out catalogs)))
            {
                error = $"Overlay '{property.Name}' has invalid catalogs.";
                return false;
            }

            if (!TryReadArgv(property.Value, out _, out error))
            {
                return false;
            }

            if (!TryReadOptionalStringList(
                    property.Value,
                    "pathArguments",
                    out var pathArguments,
                    out error))
            {
                error = $"Overlay '{property.Name}' pathArguments must be an array of strings.";
                return false;
            }

            overlays.Add(
                new ExternalToolSpec(
                    property.Name,
                    property.Name,
                    EmptyObject,
                    catalogs,
                    pathArguments: pathArguments));
        }

        return true;
    }

    private static bool TryReadArgv(
        JsonElement element,
        out Dictionary<string, string> argv,
        out string error)
    {
        argv = new Dictionary<string, string>(StringComparer.Ordinal);
        error = string.Empty;
        if (!element.TryGetProperty("argv", out var property)
            || property.ValueKind == JsonValueKind.Undefined
            || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            error = "argv must be an object of flag strings.";
            return false;
        }

        foreach (var entry in property.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(entry.Value.GetString()))
            {
                error = $"argv '{entry.Name}' must be a flag string.";
                return false;
            }

            argv[entry.Name] = entry.Value.GetString()!.Trim();
        }

        return true;
    }

    private static bool TryReadOptionalBoolean(
        JsonElement root,
        string name,
        bool defaultValue,
        out bool value,
        out string error)
    {
        value = defaultValue;
        error = string.Empty;
        if (!root.TryGetProperty(name, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = $"{name} must be a boolean.";
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }

    private static bool TryReadOptionalStringList(
        JsonElement root,
        string name,
        out IReadOnlyList<string>? values,
        out string error)
    {
        values = null;
        error = string.Empty;
        if (!root.TryGetProperty(name, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = $"{name} must be an array of strings.";
            return false;
        }

        var list = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"{name} must be an array of strings.";
                return false;
            }

            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                error = $"{name} must be an array of strings.";
                return false;
            }

            list.Add(text.Trim());
        }

        values = list;
        return true;
    }
}
