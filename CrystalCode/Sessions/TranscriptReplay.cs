using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Prompts;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;

namespace CrystalCode.Sessions;

/// <summary>
/// One replay row for the transcript viewport.
/// </summary>
public sealed record TranscriptLine(
    TranscriptKind Kind,
    string Text,
    string? ToolName = null);

/// <summary>
/// Maps a persisted transcript into viewport rows. Skips the live system prompt.
/// </summary>
public static class TranscriptReplay
{
    public static IReadOnlyList<TranscriptLine> Lines(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = new List<TranscriptLine>();
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            switch (item)
            {
                case ChatMessage message when TryMapMessage(message, out var messageKind, out var messageText):
                    if (messageText.Length > 0)
                    {
                        lines.Add(new TranscriptLine(messageKind, messageText));
                    }

                    break;
                case ToolCall call:
                    callNames[call.CallId] = call.Name;
                    lines.Add(new TranscriptLine(
                        TranscriptKind.Tool,
                        ToolCallText.Summary(call.Name, call.Arguments)));
                    break;
                case ToolResult result:
                    callNames.TryGetValue(result.CallId, out var toolName);
                    var kind = result.Status == ToolResultStatus.Success
                        ? TranscriptKind.Result
                        : TranscriptKind.Error;
                    var body = ToolResultText.Body(result.Text);
                    if (body.Length > 0)
                    {
                        lines.Add(new TranscriptLine(kind, body, toolName));
                    }

                    break;
            }
        }

        return lines;
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
