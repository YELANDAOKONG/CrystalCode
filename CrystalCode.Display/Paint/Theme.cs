using Spectre.Console;

namespace CrystalCode.Display.Paint;

/// <summary>
/// Spectre color tokens for the session shell.
/// Mineral, one accent, no Demo rainbow chrome. The built-in palette is the
/// default; <see cref="TryApply"/> swaps in operator overrides for the process.
/// </summary>
public static class Theme
{
    // Default and dark are separate palettes so either can change without the other.
    // They hold the same colors today.
    private static readonly IReadOnlyDictionary<string, string> DefaultPalette = CreateDark();

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = CreateDark();

    private static IReadOnlyDictionary<string, string> CreateDark() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = "grey50",
            ["rule"] = "grey27",
            ["user"] = "grey84",
            ["image"] = "darkseagreen",
            ["thinking"] = "grey46",
            ["tool"] = "grey62",
            ["ok"] = "darkseagreen",
            ["fail"] = "indianred",
            ["work"] = "lightsteelblue",
            ["plan"] = "tan",
            ["custom"] = "orchid",
            ["review"] = "lightsteelblue",
            ["selected"] = "bold lightsteelblue",
            ["heading"] = "bold lightsteelblue",
            ["code"] = "grey74",
            ["codeBg"] = "grey23",
            ["diffAdded"] = "darkseagreen",
            ["diffRemoved"] = "indianred",
            ["warning"] = "tan",
            ["muted"] = "grey42",
            ["accent"] = "lightsteelblue"
        };

    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = "#6b7280",
            ["rule"] = "#d1d5db",
            ["user"] = "#1f2937",
            ["image"] = "#4d7c5f",
            ["thinking"] = "#8b929c",
            ["tool"] = "#4b5563",
            ["ok"] = "#4d7c5f",
            ["fail"] = "#b4534f",
            ["work"] = "#4a6a94",
            ["plan"] = "#a9753a",
            ["custom"] = "#8660a6",
            ["review"] = "#4a6a94",
            ["selected"] = "bold #4a6a94",
            ["heading"] = "bold #4a6a94",
            ["code"] = "#374151",
            ["codeBg"] = "#eceef1",
            ["diffAdded"] = "#4d7c5f",
            ["diffRemoved"] = "#b4534f",
            ["warning"] = "#a9753a",
            ["muted"] = "#9ca3af",
            ["accent"] = "#4a6a94"
        };

    private static IReadOnlyDictionary<string, string> _current = DefaultPalette;

    /// <summary>
    /// Selects a built-in palette: <c>default</c> and <c>dark</c> (any case) are separate palettes with the same colors, <c>light</c> suits a light terminal.
    /// </summary>
    public static bool TryUseBuiltIn(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        switch (name.ToLowerInvariant())
        {
            case "default":
                _current = DefaultPalette;
                return true;
            case "dark":
                _current = DarkPalette;
                return true;
            case "light":
                _current = LightPalette;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Token names a theme file may set.
    /// </summary>
    public static IReadOnlyCollection<string> TokenNames => DarkPalette.Keys.ToArray();

    public static string Chrome => Get("chrome");

    public static string Rule => Get("rule");

    public static string User => Get("user");

    public static string Image => Get("image");

    public static string Thinking => Get("thinking");

    public static string Tool => Get("tool");

    public static string Ok => Get("ok");

    public static string Fail => Get("fail");

    public static string Work => Get("work");

    public static string Plan => Get("plan");

    public static string Custom => Get("custom");

    public static string Review => Get("review");

    public static string Selected => Get("selected");

    public static string Heading => Get("heading");

    public static string Code => Get("code");

    public static string CodeBg => Get("codeBg");

    public static string DiffAdded => Get("diffAdded");

    public static string DiffRemoved => Get("diffRemoved");

    public static string Warning => Get("warning");

    public static string Muted => Get("muted");

    public static string Accent => Get("accent");

    /// <summary>
    /// Replaces the palette with the defaults overlaid by <paramref name="colors"/>.
    /// Nothing changes when any entry is unknown or is not a Spectre style,
    /// so markup built from a token cannot be broken by a theme file.
    /// </summary>
    public static bool TryApply(IReadOnlyDictionary<string, string> colors, out string error)
    {
        ArgumentNullException.ThrowIfNull(colors);
        error = string.Empty;
        var next = new Dictionary<string, string>(DarkPalette, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in colors)
        {
            if (!DarkPalette.ContainsKey(name))
            {
                error = "Unknown theme color  " + name;
                return false;
            }

            if (!IsStyle(value))
            {
                error = "Invalid theme color  " + name;
                return false;
            }

            next[name] = value.Trim();
        }

        // Code blocks are drawn as "[code on codeBg]". A second background in either token
        // would make that markup throw at render time.
        if (!Style.TryParse(next["code"] + " on " + next["codeBg"], out _))
        {
            error = "Invalid theme color  code and codeBg must combine as a foreground on a background";
            return false;
        }

        _current = next;
        return true;
    }

    public static void Reset() => _current = DefaultPalette;

    private static string Get(string name) => _current[name];

    private static bool IsStyle(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(['[', ']']) < 0
        && Style.TryParse(value.Trim(), out _);
}
