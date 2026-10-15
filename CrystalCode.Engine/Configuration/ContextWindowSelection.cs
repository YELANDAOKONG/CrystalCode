using System.Globalization;

using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

internal static class ContextWindowSelection
{
    private const int TokensPerK = 1_000;
    private const int TokensPerM = 1_000_000;

    public static string Format(int window) => window switch
    {
        _ when window % TokensPerM == 0 => (window / TokensPerM).ToString(CultureInfo.InvariantCulture) + "M",
        _ when window % TokensPerK == 0 => (window / TokensPerK).ToString(CultureInfo.InvariantCulture) + "K",
        _ => window.ToString(CultureInfo.InvariantCulture)
    };

    public static bool TryParse(string text, out int window)
    {
        var value = text.Trim();
        var multiplier = 1;
        if (value.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = TokensPerK;
            value = value[..^1];
        }
        else if (value.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = TokensPerM;
            value = value[..^1];
        }

        window = 0;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var count)
            || count <= 0 || count > int.MaxValue / (decimal)multiplier)
        {
            return false;
        }

        var tokens = count * multiplier;
        if (tokens != decimal.Truncate(tokens))
        {
            return false;
        }

        window = (int)tokens;
        return true;
    }

    public static IReadOnlyList<SlashCompletion> Completions(ModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.ContextWindows.Select(window =>
        {
            var label = Format(window);
            return new SlashCompletion(label.ToLowerInvariant(), "Context " + label,
                [label.ToLowerInvariant(), window.ToString(CultureInfo.InvariantCulture)]);
        }).ToArray();
    }
}
