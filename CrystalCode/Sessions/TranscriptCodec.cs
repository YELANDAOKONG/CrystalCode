using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Compaction;
using CrystalCode.Home;

namespace CrystalCode.Sessions;

/// <summary>
/// Converts live transcript items to and from the session document.
/// </summary>
public static class TranscriptCodec
{
    public static List<SessionItemDocument> Write(IEnumerable<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var documents = new List<SessionItemDocument>();
        foreach (var item in items)
        {
            switch (item)
            {
                case ChatMessage message:
                    documents.Add(
                        new SessionItemDocument
                        {
                            Kind = "message",
                            Role = message.Role.Value,
                            Text = message.Text
                        });
                    break;
                case ToolCall call:
                    documents.Add(
                        new SessionItemDocument
                        {
                            Kind = "tool_call",
                            CallId = call.CallId,
                            Name = call.Name,
                            Arguments = call.Arguments
                        });
                    break;
                case ToolResult result:
                    documents.Add(
                        new SessionItemDocument
                        {
                            Kind = "tool_result",
                            CallId = result.CallId,
                            Text = result.Text,
                            Status = result.Status.Value
                        });
                    break;
                default:
                    break;
            }
        }

        return documents;
    }

    public static List<ChatItem> Read(IEnumerable<SessionItemDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var items = new List<ChatItem>();
        foreach (var document in documents)
        {
            if (TryRead(document, out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }

    public static bool HasConversation(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            if (item is ChatMessage { Role.Value: "user" })
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

    private static bool TryRead(SessionItemDocument document, out ChatItem item)
    {
        item = null!;
        var kind = document.Kind?.Trim().ToLowerInvariant();
        try
        {
            switch (kind)
            {
                case "message":
                    if (string.IsNullOrWhiteSpace(document.Role)
                        || document.Text is null)
                    {
                        return false;
                    }

                    item = new ChatMessage(new ChatRole(document.Role), document.Text);
                    return true;
                case "tool_call":
                    if (string.IsNullOrWhiteSpace(document.CallId)
                        || string.IsNullOrWhiteSpace(document.Name)
                        || document.Arguments is null)
                    {
                        return false;
                    }

                    item = new ToolCall(document.CallId, document.Name, document.Arguments);
                    return true;
                case "tool_result":
                    if (string.IsNullOrWhiteSpace(document.CallId)
                        || document.Text is null)
                    {
                        return false;
                    }

                    var status = string.IsNullOrWhiteSpace(document.Status)
                        ? ToolResultStatus.Success
                        : new ToolResultStatus(document.Status);
                    item = new ToolResult(document.CallId, document.Text, status);
                    return true;
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
