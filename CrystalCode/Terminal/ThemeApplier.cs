using CrystalCode.Display.Paint;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

namespace CrystalCode.Terminal;

/// <summary>
/// Reads a theme from Home and makes it the palette of this process. Nothing
/// changes when the theme cannot be read or applied.
/// </summary>
internal static class ThemeApplier
{
    public static bool TryApply(CrystalHome home, string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(name);
        error = string.Empty;
        if (Theme.TryUseBuiltIn(name))
        {
            return true;
        }

        return ThemeCatalog.TryRead(home, name, Theme.TrueColor, out var colors, out error)
            && ThemeCatalog.TryReadGauges(home, name, out var gauges, out error)
            && Theme.TryApply(colors, ToGauges(gauges), out error);
    }

    private static Dictionary<string, IReadOnlyList<GaugeRule>> ToGauges(
        IReadOnlyDictionary<string, IReadOnlyList<ThemeGaugeRule>> gauges) =>
        gauges.ToDictionary(
            static gauge => gauge.Key,
            static gauge => (IReadOnlyList<GaugeRule>)
                [.. gauge.Value.Select(static rule =>
                    new GaugeRule(rule.From, rule.To, rule.Match, rule.Pattern, rule.Color))],
            StringComparer.OrdinalIgnoreCase);
}
