using System.Text.Json;

using Crystal;

namespace CrystalCode.Providers.Anthropic;

/// <summary>
/// Accumulates the usage figures of one Messages response. Anthropic reports
/// uncached input, cache writes, and cache reads as separate counts, while
/// <see cref="TokenUsage"/> has one input count that the session reads as the
/// size of the prompt. The three are therefore summed; otherwise a cache hit
/// reports only the uncached tail.
/// </summary>
internal sealed class AnthropicUsageTally
{
    private long? _input;
    private long? _cacheCreation;
    private long? _cacheRead;
    private long? _output;

    /// <summary>
    /// Merges a <c>usage</c> object. Counts are cumulative, and gateways differ
    /// in which event repeats the input side, so the largest figure wins.
    /// </summary>
    public void Add(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Raise(ref _input, ReadCount(usage, "input_tokens"));
        Raise(ref _cacheCreation, ReadCount(usage, "cache_creation_input_tokens"));
        Raise(ref _cacheRead, ReadCount(usage, "cache_read_input_tokens"));
        Raise(ref _output, ReadCount(usage, "output_tokens"));
    }

    /// <summary>
    /// Returns the usage, or null when the provider reported no input side or
    /// no output count. A missing figure is unknown, not zero.
    /// </summary>
    public TokenUsage? ToUsage()
    {
        if (_output is not { } output
            || (_input is null && _cacheCreation is null && _cacheRead is null))
        {
            return null;
        }

        return new TokenUsage(
            checked((_input ?? 0) + (_cacheCreation ?? 0) + (_cacheRead ?? 0)),
            output);
    }

    // Gateways send null for fields they do not track, so anything that is not
    // a non-negative integer counts as absent.
    private static long? ReadCount(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out var count)
            && count >= 0
                ? count
                : null;

    private static void Raise(ref long? field, long? value)
    {
        if (value is not { } reported)
        {
            return;
        }

        field = field is { } current ? Math.Max(current, reported) : reported;
    }
}
