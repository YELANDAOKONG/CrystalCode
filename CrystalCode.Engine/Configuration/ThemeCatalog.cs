using System.Text.Json;
using System.Text.RegularExpressions;

using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Theme files under <c>~/.crystal/themes/&lt;name&gt;.json</c>. Themes are Home-only,
/// so a repository cannot change how the interface looks. Each file is an object
/// of color token names to Spectre style text; an object value groups tokens
/// under a prefix, so <c>{"approval": {"full": "red"}}</c> sets <c>approval.full</c>. The engine only validates and
/// reads names; a front end decides what a color means.
/// </summary>
public static partial class ThemeCatalog
{
    public const string DefaultName = "default";

    /// <summary>
    /// Palettes the front end ships. <c>default</c> is the same as <c>dark</c>. A file with one of
    /// these names is ignored.
    /// </summary>
    public static IReadOnlyList<string> BuiltInNames { get; } = [DefaultName, "dark", "light"];

    public static bool IsBuiltIn(string name) => BuiltInNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the built-in spelling when <paramref name="name"/> matches a built-in theme
    /// ignoring case, otherwise the name unchanged.
    /// </summary>
    public static string Canonical(string name) =>
        BuiltInNames.FirstOrDefault(built => built.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

    public const string InvalidNameText =
        "A theme name uses letters, digits, dot, dash, or underscore.";

    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name) && NamePattern().IsMatch(name);

    public static bool Exists(CrystalHome home, string name)
    {
        ArgumentNullException.ThrowIfNull(home);
        return IsBuiltIn(name)
            || (IsValidName(name) && File.Exists(PathFor(home, name)));
    }

    public static IReadOnlyList<string> Names(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        var names = new List<string>(BuiltInNames);
        if (Directory.Exists(home.ThemesDirectory))
        {
            names.AddRange(Directory.EnumerateFiles(home.ThemesDirectory, "*.json")
                .Select(static path => Path.GetFileNameWithoutExtension(path))
                .Where(static name => IsValidName(name) && !IsBuiltIn(name))
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        return names;
    }

    /// <summary>
    /// Reads the color overrides of one theme file. A built-in theme has none.
    /// </summary>
    public static bool TryRead(
        CrystalHome home,
        string name,
        out IReadOnlyDictionary<string, string> colors,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        colors = new Dictionary<string, string>();
        error = string.Empty;
        if (IsBuiltIn(name))
        {
            return true;
        }

        if (!Exists(home, name))
        {
            error = "Theme not found  " + name;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(PathFor(home, name)),
                HomeJson.ProviderCatalogReadOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Theme " + name + " must be a JSON object.";
                return false;
            }

            var read = new Dictionary<string, string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    // One level of grouping, such as "approval": { "full": "red" }, reads as "approval.full".
                    foreach (var member in property.Value.EnumerateObject())
                    {
                        var key = property.Name + "." + member.Name;
                        if (member.Value.ValueKind != JsonValueKind.String)
                        {
                            error = "Theme " + name + " color " + key + " must be text.";
                            return false;
                        }

                        read[key] = member.Value.GetString()!;
                    }

                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    error = "Theme " + name + " color " + property.Name + " must be text.";
                    return false;
                }

                read[property.Name] = property.Value.GetString()!;
            }

            colors = read;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            error = "Theme " + name + " cannot be read  " + exception.Message;
            return false;
        }
    }

    private static string PathFor(CrystalHome home, string name) =>
        Path.Combine(home.ThemesDirectory, name + ".json");

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex NamePattern();
}
