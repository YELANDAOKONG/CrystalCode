using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Finds and reads the operator provider catalog files: <c>providers.json</c>,
/// then <c>providers/*.json</c>, then <c>providers/&lt;category&gt;/*.json</c>.
/// Every file uses the <c>providers.json</c> shape. A category directory only
/// groups files on disk: provider identity comes from the keys inside each file,
/// and one provider name may be defined in only one file. Directories below a
/// category are not read.
/// </summary>
internal static class ProviderFiles
{
    private const string FilePattern = "*.json";

    /// <summary>
    /// Lists the provider files in load order. An empty list means the operator
    /// catalog has no file source, so the legacy <c>config.json</c> field applies.
    /// </summary>
    public static IReadOnlyList<string> Discover(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);

        var paths = new List<string>();
        if (File.Exists(home.ProvidersPath))
        {
            paths.Add(home.ProvidersPath);
        }

        if (!Directory.Exists(home.ProvidersDirectory))
        {
            return paths;
        }

        paths.AddRange(EnumerateJson(home.ProvidersDirectory));
        foreach (var category in Directory.EnumerateDirectories(home.ProvidersDirectory)
            .Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(category);
            if (!ExternalToolNames.IsDirectoryName(name))
            {
                throw new InvalidOperationException(
                    $"Provider directory '{name}' is not a valid name. Use 1-64 characters, "
                    + "start with a letter, then letters, digits, '.', '_', or '-'.");
            }

            paths.AddRange(EnumerateJson(category));
        }

        return paths;
    }

    public static IReadOnlyList<ProviderDefinition> Read(CrystalHome home, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(paths);

        var definitions = new List<ProviderDefinition>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var shown = Path.GetRelativePath(home.Root, path);
            var fromFile = ReadFile(path, shown);
            foreach (var name in fromFile.Select(static definition => definition.Name.Value)
                .Distinct(StringComparer.Ordinal))
            {
                if (owners.TryGetValue(name, out var first))
                {
                    throw new InvalidOperationException(
                        $"Provider '{name}' is defined in both '{first}' and '{shown}'. "
                        + "Define each provider name in one file and use an array for several protocols.");
                }

                owners[name] = shown;
            }

            definitions.AddRange(fromFile);
        }

        return definitions;
    }

    private static IReadOnlyList<ProviderDefinition> ReadFile(string path, string shown)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path),
                HomeJson.ProviderCatalogReadOptions);
            return SettingsMapper.ReadProviders(document.RootElement);
        }
        catch (Exception exception)
            when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Provider file '{shown}' is invalid: {exception.Message}",
                exception);
        }
    }

    private static IEnumerable<string> EnumerateJson(string directory) =>
        Directory.EnumerateFiles(directory, FilePattern).Order(StringComparer.Ordinal);
}
