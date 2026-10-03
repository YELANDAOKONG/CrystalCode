using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Sessions;

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
                case ChatReasoningItem reasoning:
                    documents.Add(WriteReasoning(reasoning));
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
                case "reasoning":
                    return TryReadReasoning(document, out item);
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static SessionItemDocument WriteReasoning(ChatReasoningItem reasoning)
    {
        var segments = new List<SessionReasoningSegment>();
        foreach (var segment in reasoning.Content.TextSegments)
        {
            segments.Add(new SessionReasoningSegment
            {
                Text = segment.Text,
                Kind = segment.Kind.Value
            });
        }

        var state = reasoning.Content.State;
        return new SessionItemDocument
        {
            Kind = "reasoning",
            Segments = segments.Count == 0 ? null : segments,
            StateFormat = state?.Format,
            StateData = state is null ? null : Convert.ToBase64String(state.Data.Span)
        };
    }

    private static bool TryReadReasoning(SessionItemDocument document, out ChatItem item)
    {
        item = null!;
        var segments = new List<ReasoningText>();
        if (document.Segments is not null)
        {
            foreach (var segment in document.Segments)
            {
                if (string.IsNullOrEmpty(segment.Text)
                    || string.IsNullOrWhiteSpace(segment.Kind))
                {
                    continue;
                }

                segments.Add(new ReasoningText(segment.Text, new ReasoningTextKind(segment.Kind)));
            }
        }

        OpaqueReasoningState? state = null;
        if (!string.IsNullOrWhiteSpace(document.StateFormat)
            && !string.IsNullOrWhiteSpace(document.StateData))
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(document.StateData);
            }
            catch (FormatException)
            {
                return false;
            }

            if (data.Length == 0)
            {
                return false;
            }

            state = new OpaqueReasoningState(document.StateFormat, data);
        }

        if (segments.Count == 0 && state is null)
        {
            return false;
        }

        item = new ChatReasoningItem(new ReasoningContent(segments, state));
        return true;
    }
}
