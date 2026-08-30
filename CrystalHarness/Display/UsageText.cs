using Crystal;

namespace CrystalHarness.Display;

/// <summary>
/// Context and elapsed labels for the status bar.
/// </summary>
public static class UsageText
{
    public static string Format(TokenUsage? usage, int contextWindow)
    {
        if (usage is null)
        {
            return "ctx --";
        }

        var percent = contextWindow <= 0
            ? 0
            : Math.Clamp((int)(usage.TotalTokenCount * 100 / contextWindow), 0, 99);
        return $"ctx {percent}%  ·  {FormatNumber(usage.InputTokenCount)} in / {FormatNumber(usage.OutputTokenCount)} out";
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 10)
        {
            return $"{elapsed.TotalSeconds:0.0}s";
        }

        return $"{(int)elapsed.TotalSeconds}s";
    }

    private static string FormatNumber(long value)
    {
        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000.0:0.#}M";
        }

        if (value >= 10_000)
        {
            return $"{value / 1_000.0:0.#}k";
        }

        return value.ToString();
    }
}
