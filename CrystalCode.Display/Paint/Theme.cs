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
            ["approval.plan"] = "grey50",
            ["approval.default"] = "grey50",
            ["approval.edit"] = "grey50",
            ["approval.review"] = "grey50",
            ["approval.audit"] = "grey50",
            ["approval.full"] = "darkorange",
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
            ["approval.plan"] = "#6b7280",
            ["approval.default"] = "#6b7280",
            ["approval.edit"] = "#6b7280",
            ["approval.review"] = "#6b7280",
            ["approval.audit"] = "#6b7280",
            ["approval.full"] = "#c2570c",
            ["muted"] = "#9ca3af",
            ["accent"] = "#4a6a94"
        };

    private const int MaximumGaugeBands = 16;

    private const string ContextGauge = "context";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<GaugeBand>> NoGauges =
        new Dictionary<string, IReadOnlyList<GaugeBand>>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> _current = DefaultPalette;

    private static IReadOnlyDictionary<string, IReadOnlyList<GaugeBand>> _gauges = NoGauges;

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
                _gauges = NoGauges;
                return true;
            case "dark":
                _current = DarkPalette;
                _gauges = NoGauges;
                return true;
            case "light":
                _current = LightPalette;
                _gauges = NoGauges;
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

    public static string ApprovalPlan => Get("approval.plan");

    public static string ApprovalDefault => Get("approval.default");

    public static string ApprovalEdit => Get("approval.edit");

    public static string ApprovalReview => Get("approval.review");

    public static string ApprovalAudit => Get("approval.audit");

    public static string ApprovalFull => Get("approval.full");

    /// <summary>
    /// Color for an approval-mode label such as <c>Full</c>. Unknown labels use <see cref="Chrome"/>.
    /// </summary>
    public static string ForApproval(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return label.ToLowerInvariant() switch
        {
            "plan" => ApprovalPlan,
            "default" => ApprovalDefault,
            "edit" => ApprovalEdit,
            "review" => ApprovalReview,
            "audit" => ApprovalAudit,
            "full" => ApprovalFull,
            _ => Chrome
        };
    }

    /// <summary>
    /// Gauge names a theme file may set ranges for.
    /// </summary>
    public static IReadOnlyCollection<string> GaugeNames { get; } = [ContextGauge];

    /// <summary>
    /// Style for a context usage percentage. A theme's <c>context</c> ranges
    /// are tried in order and the first one holding the value wins; a value
    /// that no range holds uses <see cref="Chrome"/>. Without ranges, 60 and
    /// above is <see cref="Warning"/> and 80 and above is <see cref="Fail"/>.
    /// </summary>
    public static string ForContext(double percent)
    {
        if (_gauges.TryGetValue(ContextGauge, out var bands))
        {
            foreach (var band in bands)
            {
                if (percent >= band.From && percent <= band.To)
                {
                    return band.Style;
                }
            }

            return Chrome;
        }

        return percent >= 80 ? Fail : percent >= 60 ? Warning : Chrome;
    }

    public static string Muted => Get("muted");

    public static string Accent => Get("accent");

    /// <summary>
    /// Replaces the palette with the defaults overlaid by <paramref name="colors"/>.
    /// Nothing changes when any entry is unknown or is not a Spectre style,
    /// so markup built from a token cannot be broken by a theme file.
    /// </summary>
    public static bool TryApply(IReadOnlyDictionary<string, string> colors, out string error) =>
        TryApply(colors, NoGauges, out error);

    /// <summary>
    /// Like <see cref="TryApply(IReadOnlyDictionary{string, string}, out string)"/>, and also
    /// sets gauge ranges. Nothing changes when a gauge or range is invalid.
    /// </summary>
    public static bool TryApply(
        IReadOnlyDictionary<string, string> colors,
        IReadOnlyDictionary<string, IReadOnlyList<GaugeBand>> gauges,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(gauges);
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

        if (!TryReadGauges(gauges, out var nextGauges, out error))
        {
            return false;
        }

        _current = next;
        _gauges = nextGauges;
        return true;
    }

    public static void Reset()
    {
        _current = DefaultPalette;
        _gauges = NoGauges;
    }

    private static bool TryReadGauges(
        IReadOnlyDictionary<string, IReadOnlyList<GaugeBand>> gauges,
        out IReadOnlyDictionary<string, IReadOnlyList<GaugeBand>> next,
        out string error)
    {
        error = string.Empty;
        next = NoGauges;
        var read = new Dictionary<string, IReadOnlyList<GaugeBand>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, bands) in gauges)
        {
            if (!GaugeNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                error = "Unknown theme gauge  " + name;
                return false;
            }

            if (bands.Count > MaximumGaugeBands)
            {
                error = "Invalid theme gauge  " + name + " has more than " + MaximumGaugeBands + " ranges";
                return false;
            }

            foreach (var band in bands)
            {
                if (!(band.From >= 0 && band.To <= 100 && band.From <= band.To))
                {
                    error = "Invalid theme gauge  " + name + " ranges need 0 <= from <= to <= 100";
                    return false;
                }

                if (!IsStyle(band.Style))
                {
                    error = "Invalid theme gauge  " + name + " color " + band.Style;
                    return false;
                }
            }

            read[name] = [.. bands.Select(static band => band with { Style = band.Style.Trim() })];
        }

        next = read;
        return true;
    }

    private static string Get(string name) => _current[name];

    private static bool IsStyle(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(['[', ']']) < 0
        && Style.TryParse(value.Trim(), out _);
}
