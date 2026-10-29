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
    /// The one theme file member that holds ranges instead of colors.
    /// </summary>
    public const string GaugesKey = "gauges";

    private const int MaximumRules = 16;

    private const int MaximumMatches = 32;

    private const int MaximumPatternLength = 200;

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
                if (property.Name.Equals(GaugesKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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

    /// <summary>
    /// Reads the gauge rules of one theme file, such as
    /// <c>{"gauges": {"context": [{"from": 80, "color": "red"}],
    /// "thinking": [{"match": "high", "color": "orange1"}]}}</c>. Each gauge
    /// is a list in priority order: the first rule that holds a value wins. A
    /// rule has <c>from</c> and <c>to</c> (default 0 and 100), or <c>match</c>
    /// (text or a list of texts, <c>*</c> for all), or <c>regex</c>. A built-in
    /// theme has none.
    /// </summary>
    public static bool TryReadGauges(
        CrystalHome home,
        string name,
        out IReadOnlyDictionary<string, IReadOnlyList<ThemeGaugeRule>> gauges,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        var read = new Dictionary<string, IReadOnlyList<ThemeGaugeRule>>(StringComparer.OrdinalIgnoreCase);
        gauges = read;
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

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals(GaugesKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    error = "Theme " + name + " " + GaugesKey + " must be an object.";
                    return false;
                }

                foreach (var gauge in property.Value.EnumerateObject())
                {
                    if (!TryReadRules(name, gauge, out var rules, out error))
                    {
                        return false;
                    }

                    read[gauge.Name] = rules;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            error = "Theme " + name + " cannot be read  " + exception.Message;
            return false;
        }
    }

    private static bool TryReadRules(
        string theme,
        JsonProperty gauge,
        out IReadOnlyList<ThemeGaugeRule> rules,
        out string error)
    {
        var read = new List<ThemeGaugeRule>();
        rules = read;
        error = string.Empty;
        var label = "Theme " + theme + " gauge " + gauge.Name;
        if (gauge.Value.ValueKind != JsonValueKind.Array)
        {
            error = label + " must be a list of rules.";
            return false;
        }

        if (gauge.Value.GetArrayLength() > MaximumRules)
        {
            error = label + " has more than " + MaximumRules + " rules.";
            return false;
        }

        foreach (var item in gauge.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = label + " rules must be objects.";
                return false;
            }

            if (!TryReadRule(label, item, out var rule, out error))
            {
                return false;
            }

            read.Add(rule!);
        }

        return true;
    }

    private static bool TryReadRule(string label, JsonElement item, out ThemeGaugeRule? rule, out string error)
    {
        rule = null;
        error = string.Empty;
        double? from = null;
        double? to = null;
        var match = new List<string>();
        string? pattern = null;
        string? color = null;
        foreach (var member in item.EnumerateObject())
        {
            switch (member.Name)
            {
                case "from" or "to":
                    if (member.Value.ValueKind != JsonValueKind.Number
                        || !member.Value.TryGetDouble(out var number)
                        || !double.IsFinite(number))
                    {
                        error = label + " " + member.Name + " must be a number.";
                        return false;
                    }

                    if (member.Name == "from")
                    {
                        from = number;
                    }
                    else
                    {
                        to = number;
                    }

                    break;
                case "match":
                    if (!TryReadMatch(member.Value, match))
                    {
                        error = label + " match must be text or a list of 1 to " + MaximumMatches + " texts.";
                        return false;
                    }

                    break;
                case "regex":
                    if (member.Value.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(member.Value.GetString())
                        || member.Value.GetString()!.Length > MaximumPatternLength)
                    {
                        error = label + " regex must be text of 1 to " + MaximumPatternLength + " characters.";
                        return false;
                    }

                    pattern = member.Value.GetString();
                    break;
                case "color":
                    if (member.Value.ValueKind != JsonValueKind.String)
                    {
                        error = label + " color must be text.";
                        return false;
                    }

                    color = member.Value.GetString();
                    break;
                default:
                    error = label + " rule member " + member.Name
                        + " is not valid. Use from, to, match, regex, and color.";
                    return false;
            }
        }

        if (color is null)
        {
            error = label + " rules need a color.";
            return false;
        }

        var isValue = match.Count > 0 || pattern is not null;
        if (match.Count > 0 && pattern is not null)
        {
            error = label + " rules use match or regex, not both.";
            return false;
        }

        if (isValue && (from is not null || to is not null))
        {
            error = label + " rules use from and to, or match or regex, not both.";
            return false;
        }

        if (!isValue)
        {
            from ??= 0;
            to ??= 100;
        }

        rule = new ThemeGaugeRule(from, to, match, pattern, color);
        return true;
    }

    private static bool TryReadMatch(JsonElement value, List<string> match)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return AddMatch(value, match);
        }

        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is 0 or > MaximumMatches)
        {
            return false;
        }

        return value.EnumerateArray().All(item => AddMatch(item, match));
    }

    private static bool AddMatch(JsonElement item, List<string> match)
    {
        if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
        {
            return false;
        }

        match.Add(item.GetString()!.Trim());
        return true;
    }

    private static string PathFor(CrystalHome home, string name) =>
        Path.Combine(home.ThemesDirectory, name + ".json");

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex NamePattern();
}
