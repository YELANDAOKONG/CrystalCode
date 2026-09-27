using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Sessions;

namespace CrystalCode.Compaction;

/// <summary>
/// Serializes transcript items for the compaction model. Truncates tool output.
/// </summary>
public static class CompactionText
{
    public const int ToolOutputMaxChars = 2_000;

    public static string Conversation(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var parts = new List<string>();
        foreach (var item in items)
        {
            var piece = Item(item);
            if (piece.Length > 0)
            {
                parts.Add(piece);
            }
        }

        return string.Join("\n\n", parts);
    }

    public static string Item(ChatItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item switch
        {
            ChatMessage message when message.Role == ChatRole.User =>
                "[User]: " + ImageMarkerText.Display(message.Text),
            ChatMessage message when message.Role == ChatRole.Assistant =>
                "[Assistant]: " + ImageMarkerText.Display(message.Text),
            ChatMessage message when CompactionSelection.IsSummary(message) =>
                ImageMarkerText.Display(message.Text),
            ToolCall call => "[Assistant tool call]: " + call.Name + "(" + call.Arguments + ")",
            ToolResult result when result.Status == ToolResultStatus.Failure =>
                "[Tool error]: " + Truncate(ImageMarkerText.Display(result.Text)),
            ToolResult result when result.Text == ContextCompactor.OmittedResultText =>
                "[Tool result]: [Old tool result content cleared]",
            ToolResult result => "[Tool result]: " + Truncate(ImageMarkerText.Display(result.Text)),
            _ => string.Empty
        };
    }

    public static string Truncate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= ToolOutputMaxChars)
        {
            return value;
        }

        return value[..ToolOutputMaxChars] + "\n[truncated]";
    }
}
