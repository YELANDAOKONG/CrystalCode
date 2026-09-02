using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Compaction;
using CrystalCode.Prompts;
using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Filters transcript items for operator exports.
/// </summary>
public static class TranscriptExport
{
    public static IReadOnlyList<ChatItem> ConversationItems(IReadOnlyList<ChatItem> transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var skipLiveSystem = true;
        var items = new List<ChatItem>();
        foreach (var item in transcript)
        {
            if (skipLiveSystem
                && item is ChatMessage message
                && message.Role == ChatRole.System
                && !CompactionSelection.IsSummary(message))
            {
                skipLiveSystem = false;
                continue;
            }

            items.Add(item);
        }

        return items;
    }

    public static string RenderMarkdown(
        SessionExportMetadata metadata,
        IReadOnlyList<ChatItem> items,
        IReadOnlyList<TodoItem> todos,
        string? systemText)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(todos);
        var lines = new List<string>
        {
            "# Crystal Code session " + metadata.SessionId,
            string.Empty,
            "Workspace: " + metadata.Workspace,
            "Model: " + metadata.ModelLine,
            "Prompt set: " + metadata.PromptSet,
            "Plan mode: " + (metadata.PlanMode ? "yes" : "no"),
            "Exported: " + metadata.ExportedUtc.ToString("O"),
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(systemText))
        {
            lines.Add("## System");
            lines.Add(string.Empty);
            lines.Add(systemText.Trim());
            lines.Add(string.Empty);
        }

        if (todos.Count > 0)
        {
            lines.Add("## Todos");
            lines.Add(string.Empty);
            foreach (var todo in todos)
            {
                lines.Add("- " + TodoList.StatusMark(todo.Status) + " " + todo.Content);
            }

            lines.Add(string.Empty);
        }

        lines.Add("## Transcript");
        lines.Add(string.Empty);
        foreach (var item in items)
        {
            AppendMarkdownItem(lines, item);
        }

        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    private static void AppendMarkdownItem(List<string> lines, ChatItem item)
    {
        switch (item)
        {
            case ChatMessage message when message.Role == ChatRole.User:
                lines.Add("### User");
                lines.Add(string.Empty);
                lines.Add(message.Text.Trim());
                lines.Add(string.Empty);
                break;
            case ChatMessage message when message.Role == ChatRole.Assistant:
                lines.Add("### Assistant");
                lines.Add(string.Empty);
                lines.Add(message.Text.Trim());
                lines.Add(string.Empty);
                break;
            case ChatMessage message when CompactionSelection.IsSummary(message):
                lines.Add("### " + CompactionPrompt.Marker);
                lines.Add(string.Empty);
                lines.Add(message.Text[CompactionPrompt.Marker.Length..].Trim());
                lines.Add(string.Empty);
                break;
            case ToolCall call:
                lines.Add("### Tool: " + call.Name);
                lines.Add(string.Empty);
                lines.Add("```json");
                lines.Add(call.Arguments.Trim());
                lines.Add("```");
                lines.Add(string.Empty);
                break;
            case ToolResult result:
                lines.Add(result.Status == ToolResultStatus.Success
                    ? "### Tool result"
                    : "### Tool error");
                lines.Add(string.Empty);
                lines.Add("```");
                lines.Add(result.Text.Trim());
                lines.Add("```");
                lines.Add(string.Empty);
                break;
        }
    }
}
