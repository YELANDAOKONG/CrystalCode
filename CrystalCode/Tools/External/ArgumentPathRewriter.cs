using System.Text.Json;

namespace CrystalCode.Tools.External;

/// <summary>
/// Rewrites declared path arguments to absolute paths using the workspace fence.
/// </summary>
internal static class ArgumentPathRewriter
{
    public static bool TryRewrite(
        string arguments,
        IReadOnlyList<string> pathArguments,
        Workspace workspace,
        out string rewritten,
        out string error)
    {
        rewritten = arguments;
        error = string.Empty;
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(pathArguments);
        ArgumentNullException.ThrowIfNull(workspace);
        if (pathArguments.Count == 0)
        {
            return true;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(arguments);
        }
        catch (JsonException)
        {
            error = "Arguments must be a JSON object.";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Arguments must be a JSON object.";
                return false;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!ContainsName(pathArguments, property.Name)
                        || property.Value.ValueKind != JsonValueKind.String)
                    {
                        property.WriteTo(writer);
                        continue;
                    }

                    var path = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        error = $"Argument '{property.Name}' must be a path string.";
                        return false;
                    }

                    if (!workspace.TryGetFullPath(path, out var fullPath, out error))
                    {
                        return false;
                    }

                    writer.WriteString(property.Name, fullPath);
                }

                writer.WriteEndObject();
            }

            rewritten = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
    }

    private static bool ContainsName(IReadOnlyList<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
