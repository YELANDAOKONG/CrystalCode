using Crystal;
using Crystal.Chat;

namespace CrystalCode.Engine.Compaction;

/// <summary>
/// A provider-reported context size tied to the last transcript item it
/// covered. Only items added after <see cref="Last"/> need a local estimate.
/// The anchor is found again by reference, so a rewritten transcript stops
/// matching it instead of reusing a size that no longer applies.
/// </summary>
/// <param name="Tokens">Input plus output tokens the provider reported for one round.</param>
/// <param name="Last">The final item that round left in the transcript.</param>
public sealed record ContextAnchor(long Tokens, ChatItem Last)
{
    /// <summary>
    /// Anchors the round that just ended, or returns null when the endpoint
    /// reported no usage or the transcript is empty.
    /// </summary>
    public static ContextAnchor? From(TokenUsage? usage, IReadOnlyList<ChatItem> transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (usage is null || transcript.Count == 0)
        {
            return null;
        }

        return new ContextAnchor(usage.TotalTokenCount, transcript[^1]);
    }
}
