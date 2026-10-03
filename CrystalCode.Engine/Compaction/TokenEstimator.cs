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

    private static int Reasoning(ChatReasoningItem item)
    {
        var total = 0;
        foreach (var segment in item.Content.TextSegments)
        {
            total += Text(segment.Text);
        }

        if (item.Content.State is { } state)
        {
            total += Characters(state.Data.Length);
        }

        return total;
    }

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
