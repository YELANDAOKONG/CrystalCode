using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Compaction;

namespace CrystalHarness.Approvals;

/// <summary>
/// Compact conversation evidence for approval review.
/// Keeps the first and latest user turns as authorization anchors.
/// </summary>
public static class ReviewTranscript
{
    public const int MaxMessageTranscriptTokens = 4_000;

    public const int MaxToolTranscriptTokens = 2_000;

    public const int MaxMessageEntryTokens = 2_000;

    public const int MaxToolEntryTokens = 1_000;

    public const int RecentNonUserLimit = 40;

    public const string OmittedNote = "Some conversation entries were omitted.";

    public static bool HasAuthorization(IReadOnlyList<ChatItem>? items)
    {
        if (items is null)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item is ChatMessage { Role.Value: "user" } user
                && !string.IsNullOrWhiteSpace(user.Text))
            {
                return true;
            }

            if (CompactionSelection.IsSummary(item))
            {
                return true;
            }
        }

        return false;
    }

    public static string Render(IReadOnlyList<ChatItem>? items)
    {
        if (items is null || items.Count == 0)
        {
            return string.Empty;
        }

        var entries = Collect(items);
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var (lines, omitted) = Select(entries);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var text = string.Join("\n\n", lines);
        if (omitted)
        {
            return text + "\n\n" + OmittedNote;
        }

        return text;
    }

    private static List<Entry> Collect(IReadOnlyList<ChatItem> items)
    {
        var entries = new List<Entry>();
        for (var i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case ChatMessage message when CompactionSelection.IsSummary(message):
                    if (!string.IsNullOrWhiteSpace(message.Text))
                    {
                        entries.Add(new Entry(EntryKind.Summary, message.Text.Trim()));
                    }

                    break;
                case ChatMessage message when i == 0 && message.Role == ChatRole.System:
                    break;
                case ChatMessage { Role.Value: "user" } user
                    when !string.IsNullOrWhiteSpace(user.Text):
                    entries.Add(new Entry(EntryKind.User, user.Text.Trim()));
                    break;
                case ChatMessage { Role.Value: "assistant" } assistant
                    when !string.IsNullOrWhiteSpace(assistant.Text):
                    entries.Add(new Entry(EntryKind.Assistant, assistant.Text.Trim()));
                    break;
                case ToolCall call:
                    entries.Add(
                        new Entry(
                            EntryKind.Tool,
                            "[Assistant tool call]: " + call.Name + "(" + call.Arguments + ")"));
                    break;
                case ToolResult result:
                    entries.Add(new Entry(EntryKind.Tool, FormatResult(result)));
                    break;
                default:
                    break;
            }
        }

        return entries;
    }

    private static (List<string> Lines, bool Omitted) Select(IReadOnlyList<Entry> entries)
    {
        var rendered = new string[entries.Count];
        var tokens = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            rendered[i] = RenderEntry(entries[i]);
            tokens[i] = TokenEstimator.Text(rendered[i]);
        }

        var included = new bool[entries.Count];
        var messageTokens = 0;
        var toolTokens = 0;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kind != EntryKind.Summary)
            {
                continue;
            }

            included[i] = true;
            messageTokens += tokens[i];
        }

        var userIndices = new List<int>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kind == EntryKind.User)
            {
                userIndices.Add(i);
            }
        }

        if (userIndices.Count > 0)
        {
            var firstUser = userIndices[0];
            included[firstUser] = true;
            messageTokens += tokens[firstUser];
        }

        if (userIndices.Count > 1)
        {
            var lastUser = userIndices[^1];
            if (!included[lastUser]
                && messageTokens + tokens[lastUser] <= MaxMessageTranscriptTokens)
            {
                included[lastUser] = true;
                messageTokens += tokens[lastUser];
            }
        }

        for (var u = userIndices.Count - 1; u >= 0; u--)
        {
            var index = userIndices[u];
            if (included[index])
            {
                continue;
            }

            if (messageTokens + tokens[index] > MaxMessageTranscriptTokens)
            {
                continue;
            }

            included[index] = true;
            messageTokens += tokens[index];
        }

        var retainedNonUser = 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (included[i] || entries[i].Kind == EntryKind.User)
            {
                continue;
            }

            if (retainedNonUser >= RecentNonUserLimit)
            {
                continue;
            }

            var isTool = entries[i].Kind == EntryKind.Tool;
            var withinBudget = isTool
                ? toolTokens + tokens[i] <= MaxToolTranscriptTokens
                : messageTokens + tokens[i] <= MaxMessageTranscriptTokens;
            if (!withinBudget)
            {
                continue;
            }

            included[i] = true;
            retainedNonUser++;
            if (isTool)
            {
                toolTokens += tokens[i];
            }
            else
            {
                messageTokens += tokens[i];
            }
        }

        var lines = new List<string>();
        var omitted = false;
        for (var i = 0; i < entries.Count; i++)
        {
            if (included[i])
            {
                lines.Add(rendered[i]);
                continue;
            }

            omitted = true;
        }

        return (lines, omitted);
    }

    private static string RenderEntry(Entry entry)
    {
        var raw = entry.Kind switch
        {
            EntryKind.Summary => entry.Text,
            EntryKind.User => "[User]: " + entry.Text,
            EntryKind.Assistant => "[Assistant]: " + entry.Text,
            EntryKind.Tool => entry.Text,
            _ => entry.Text
        };
        var cap = entry.Kind == EntryKind.Tool
            ? MaxToolEntryTokens
            : MaxMessageEntryTokens;
        return Truncate(raw, cap);
    }

    private static string FormatResult(ToolResult result)
    {
        if (result.Status == ToolResultStatus.Failure)
        {
            return "[Tool error]: " + result.Text;
        }

        return "[Tool result]: " + result.Text;
    }

    private static string Truncate(string text, int maxTokens)
    {
        var maxChars = (int)(maxTokens * TokenEstimator.CharactersPerToken);
        if (text.Length <= maxChars)
        {
            return text;
        }

        const string suffix = "\n[truncated]";
        var keep = Math.Max(0, maxChars - suffix.Length);
        return text[..keep] + suffix;
    }

    private enum EntryKind
    {
        Summary,
        User,
        Assistant,
        Tool
    }

    private sealed record Entry(EntryKind Kind, string Text);
}
