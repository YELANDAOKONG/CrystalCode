using System.Text;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Prompts;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// Renders a transcript as numbered entries for the checker. Entries keep
/// their number when older ones are left out, so a citation always names the
/// same entry.
/// </summary>
internal static class PursueConversation
{
    public const int EntryMaxCharacters = 16_000;

    public const string SummaryLabel = "[Summary of earlier entries, not citable]";

    public const string OmittedNote = "[Earlier entries were omitted to fit the context.]";

    public static PursueRendering Render(IReadOnlyList<ChatItem> items, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(items);
        // A compaction summary is context, not evidence, so it carries no number.
        var entries = new List<(int? Number, string Text)>();
        var number = 0;
        foreach (var item in items)
        {
            var text = Entry(item);
            if (text.Length == 0)
            {
                continue;
            }

            entries.Add((CompactionSelection.IsSummary(item) ? null : ++number, text));
        }

        var shown = new List<string>();
        var numbers = new HashSet<int>();
        var used = 0;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var line = (entries[index].Number is int cited ? "[" + cited + "] " : SummaryLabel + " ") + entries[index].Text;
            if (used + line.Length > maxCharacters)
            {
                break;
            }

            used += line.Length + 2;
            shown.Add(line);
            if (entries[index].Number is int shownNumber)
            {
                numbers.Add(shownNumber);
            }
        }

        shown.Reverse();
        if (entries.Count > 0 && shown.Count == 0)
        {
            return new PursueRendering(string.Empty, numbers, Fits: false, Omitted: true);
        }

        var omitted = shown.Count < entries.Count;
        var text2 = string.Join("\n\n", shown);
        return new PursueRendering(omitted ? OmittedNote + "\n\n" + text2 : text2, numbers, Fits: true, Omitted: omitted);
    }

    private static string Entry(ChatItem item)
    {
        switch (item)
        {
            case ToolResult result:
                var label = result.Status == ToolResultStatus.Failure ? "[Tool error]: " : "[Tool result]: ";
                return label + Cap(ImageMarkerText.Display(result.Text), PursuePrompt.ToolOutputMaxChars);
            case ChatMessage message when message.Role == ChatRole.System && !CompactionSelection.IsSummary(message):
                return string.Empty;
            case ChatReasoningItem:
                return string.Empty;
            default:
                return Cap(CompactionText.Item(item), EntryMaxCharacters);
        }
    }

    private static string Cap(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        var builder = new StringBuilder(text[..limit]);
        builder.Append("\n[truncated]");
        return builder.ToString();
    }
}
