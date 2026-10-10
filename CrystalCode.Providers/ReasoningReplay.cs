using Crystal.Multimodal;
using Crystal.Reasoning;

namespace CrystalCode.Providers;

/// <summary>
/// Decides whether one reasoning block can replay as provider-native opaque
/// state, and exposes its readable text for providers that cannot consume
/// that state. Switching models keeps the conversation: the new adapter
/// replays readable reasoning as assistant text instead of failing.
/// </summary>
internal static class ReasoningReplay
{
    public static bool IsReplayable(ReasoningContent content, string format)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        return content.State is { } state
            && string.Equals(state.Format, format, StringComparison.Ordinal);
    }

    public static bool IsReplayable(MultimodalReasoningContent content, string format)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        return content.State is { } state
            && string.Equals(state.Format, format, StringComparison.Ordinal);
    }

    public static string ReadableText(ReasoningContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parts = new List<string>();
        foreach (var segment in content.TextSegments)
        {
            if (segment.Text.Length > 0)
            {
                parts.Add(segment.Text);
            }
        }

        return Join(parts);
    }

    public static string ReadableText(MultimodalReasoningContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parts = new List<string>();
        foreach (var part in content.Parts)
        {
            if (part.Content is TextContent text && text.Text.Length > 0)
            {
                parts.Add(text.Text);
            }
        }

        return Join(parts);
    }

    private static string Join(List<string> parts) =>
        parts.Count == 0 ? string.Empty : string.Join("\n", parts);
}
