using Crystal;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Context and elapsed labels for the status bar.
/// </summary>
public static class UsageText
{
    private const int PercentTenths = 10;

    private const int FullPercentTenths = 100 * PercentTenths;

    public static string FormatWindow(int contextWindow) =>
        contextWindow <= 0 ? string.Empty : "Context " + Configuration.ContextWindowSelection.Format(contextWindow);

    public static string Format(TokenUsage? usage, int contextWindow)
        => Format(usage, usage, contextWindow);

    public static string Format(
        TokenUsage? contextUsage,
        TokenUsage? cumulativeUsage,
        int contextWindow)
    {
        var context = FormatContext(contextUsage, contextWindow);
        if (cumulativeUsage is null)
        {
            return context;
        }

        return $"{context}  ·  {FormatNumber(cumulativeUsage.InputTokenCount)} IN / "
            + $"{FormatNumber(cumulativeUsage.OutputTokenCount)} OUT";
    }

    private static string FormatContext(TokenUsage? usage, int contextWindow) =>
        FormatContextUsed(usage, contextWindow);

    private static int ContextTenths(TokenUsage usage, int contextWindow)
    {
        if (contextWindow <= 0 || usage.TotalTokenCount <= 0)
        {
            return 0;
        }

        var tenths = (double)usage.TotalTokenCount / contextWindow * FullPercentTenths;
        return Math.Clamp(
            (int)Math.Round(tenths, MidpointRounding.AwayFromZero),
            0,
            FullPercentTenths);
    }

    private static string FormatPercent(int tenths)
    {
        var whole = tenths / PercentTenths;
        var fraction = tenths % PercentTenths;
        return $"{whole}.{fraction}";
    }

    public static string FormatTotal(TokenUsage? usage) =>
        usage is null ? string.Empty : FormatNumber(usage.TotalTokenCount) + " Total";

    public static string FormatContextUsed(TokenUsage? usage, int contextWindow) =>
        usage is null ? "CTX --" : $"CTX {FormatPercent(ContextTenths(usage, contextWindow))}%";

    public static string FormatContextLeft(TokenUsage? usage, int contextWindow) =>
        usage is null
            ? "CTX -- Left"
            : $"CTX {FormatPercent(FullPercentTenths - ContextTenths(usage, contextWindow))}% Left";

    public static string FormatContextTokens(TokenUsage? usage, int contextWindow) =>
        usage is null
            ? $"-- / {FormatNumber(contextWindow)} CTX"
            : $"{FormatNumber(usage.TotalTokenCount)} / {FormatNumber(contextWindow)} CTX";

    public static string FormatScoped(long? value, string scope) =>
        value is null ? string.Empty : $"{FormatNumber(value.Value)} {scope}";

    public static string FormatEstimate(int tokens)
    {
        if (tokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokens), tokens, "Token estimate cannot be negative.");
        }

        return "~" + FormatNumber(tokens) + " Tokens";
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
