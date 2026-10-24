using Crystal.Chat;
using Crystal.Tools;

namespace CrystalCode.Engine.Compaction;

/// <summary>
/// Local token estimate. Four characters per token, matching OpenCode.
/// </summary>
public static class TokenEstimator
{
    public const double CharactersPerToken = 4;

    public static int Characters(int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Round(count / CharactersPerToken));
    }

    public static int Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        return Characters(value.Length);
    }

    public static int Items(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Range(items, 0, items.Count);
    }

    public static int Range(IReadOnlyList<ChatItem> items, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (start < 0 || count < 0 || start + count > items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        var total = 0;
        var end = start + count;
        for (var i = start; i < end; i++)
        {
            total += Item(items[i]);
        }

        return total;
    }

    // Provider state (a thinking block, a reasoning item, or a Gemini part)
    // already carries the readable text, so adding both counts one block twice.
    private static int Reasoning(ChatReasoningItem item)
    {
        var text = 0;
        foreach (var segment in item.Content.TextSegments)
        {
            text += Text(segment.Text);
        }

        var state = item.Content.State is { } opaque ? Characters(opaque.Data.Length) : 0;
        return Math.Max(text, state);
    }

    /// <summary>
    /// Context size when the provider's last report is still in the list: that
    /// report plus an estimate of the items after it. Returns false when there
    /// is no anchor or the anchored item is gone, for example after compaction.
    /// </summary>
    public static bool TryContext(IReadOnlyList<ChatItem> items, ContextAnchor? anchor, out int tokens)
    {
        ArgumentNullException.ThrowIfNull(items);
        tokens = 0;
        if (anchor is null)
        {
            return false;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(items[i], anchor.Last))
            {
                continue;
            }

            var total = anchor.Tokens + Range(items, i + 1, items.Count - i - 1);
            tokens = (int)Math.Min(int.MaxValue, total);
            return true;
        }

        return false;
    }

    public static int Context(IReadOnlyList<ChatItem> items, ContextAnchor? anchor) =>
        TryContext(items, anchor, out var tokens) ? tokens : Items(items);

    public static int Item(ChatItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item switch
        {
            ChatMessage message => Text(message.Text),
            ToolCall call => Text(call.Name) + Text(call.Arguments),
            ToolResult result => Text(result.Text),
            ChatReasoningItem reasoning => Reasoning(reasoning),
            _ => 0
        };
    }
}
