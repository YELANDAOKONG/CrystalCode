using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Display.Paint;
using CrystalHarness.Display.Transcript;
using CrystalHarness.Prompts;

namespace CrystalHarness.Sessions;

/// <summary>
/// Maps a persisted transcript into viewport rows. Skips the live system prompt.
/// </summary>
public static class TranscriptReplay
{
    public static IReadOnlyList<(TranscriptKind Kind, string Text)> Lines(
        IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = new List<(TranscriptKind Kind, string Text)>();
        foreach (var item in items)
        {
            if (!TryMap(item, out var kind, out var text) || text.Length == 0)
            {
                continue;
            }

            lines.Add((kind, text));
        }

        return lines;
    }

    private static bool TryMap(ChatItem item, out TranscriptKind kind, out string text)
    {
        switch (item)
        {
            case ChatMessage message:
                return TryMapMessage(message, out kind, out text);
            case ToolCall call:
                kind = TranscriptKind.Tool;
                text = ToolCallText.Summary(call.Name, call.Arguments);
                return true;
            case ToolResult result:
                kind = result.Status == ToolResultStatus.Success
                    ? TranscriptKind.Result
                    : TranscriptKind.Error;
                text = ToolResultText.Body(result.Text);
                return true;
            default:
                kind = TranscriptKind.Note;
                text = string.Empty;
                return false;
        }
    }

    private static bool TryMapMessage(
        ChatMessage message,
        out TranscriptKind kind,
        out string text)
    {
        text = message.Text;
        if (message.Role == ChatRole.User)
        {
            kind = TranscriptKind.User;
            return true;
        }

        if (message.Role == ChatRole.Assistant)
        {
            kind = TranscriptKind.Assistant;
            return true;
        }

        if (message.Role == ChatRole.System
            && message.Text.StartsWith(CompactionPrompt.Marker, StringComparison.Ordinal))
        {
            kind = TranscriptKind.Note;
            return true;
        }

        kind = TranscriptKind.Note;
        text = string.Empty;
        return false;
    }
}
