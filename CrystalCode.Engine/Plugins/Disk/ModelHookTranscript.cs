using System.Globalization;
using System.Text.RegularExpressions;

using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Sessions;
using CrystalCode.Plugins.Hooks;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Projects a transcript into plugin model items and applies an accepted
/// projection back onto chat items for one outbound call.
/// </summary>
internal static class ModelHookTranscript
{
    public static Projection Project(
        IReadOnlyList<ChatItem> items,
        IReadOnlyDictionary<int, string> mediaTypes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        var names = ToolNames(items);
        var projected = new List<PluginModelItem>(items.Count);
        var origins = new Dictionary<string, ChatItem>(items.Count, StringComparer.Ordinal);
        var byId = new Dictionary<string, PluginModelItem>(items.Count, StringComparer.Ordinal);
        string? liveSystemId = null;
        for (var index = 0; index < items.Count; index++)
        {
            var id = index.ToString(CultureInfo.InvariantCulture);
            var item = ProjectItem(id, items[index], names, mediaTypes);
            projected.Add(item);
            origins.Add(id, items[index]);
            byId.Add(id, item);
            if (index == 0
                && items[0] is ChatMessage system
                && system.Role == ChatRole.System
                && !CompactionSelection.IsSummary(system))
            {
                liveSystemId = id;
            }
        }

        return new Projection(projected, origins, byId, liveSystemId);
    }

    public static bool TryAcceptRebuild(
        Projection origin,
        IReadOnlyList<PluginModelItem> current,
        IReadOnlyList<PluginModelItem> next,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        var currentById = Index(current);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in next)
        {
            if (!origin.ById.ContainsKey(item.Id) || !seen.Add(item.Id))
            {
                reason = seen.Contains(item.Id) ? "it repeated an item." : "it added an item.";
                return false;
            }

            if (!SameKind(origin.ById[item.Id], item))
            {
                reason = "it changed an item.";
                return false;
            }
        }

        if (origin.LiveSystemId is string liveId)
        {
            if (next.Count == 0 || !string.Equals(next[0].Id, liveId, StringComparison.Ordinal))
            {
                reason = "it changed the system prompt.";
                return false;
            }

            if (origin.ById[liveId] is not PluginModelMessage live
                || next[0] is not PluginModelMessage returned
                || returned.Role != live.Role
                || !string.Equals(returned.Text, live.Text, StringComparison.Ordinal)
                || !SameImages(live.Images, returned.Images))
            {
                reason = "it changed the system prompt.";
                return false;
            }
        }

        var calls = new List<string>();
        var results = new List<string>();
        var callIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < next.Count; index++)
        {
            var item = next[index];
            var source = origin.ById[item.Id];
            if (!currentById.TryGetValue(item.Id, out var allowed))
            {
                reason = "it added an item.";
                return false;
            }

            switch (item)
            {
                case PluginModelMessage message when source is PluginModelMessage original:
                    if (message.Role != original.Role)
                    {
                        reason = "it changed a message role.";
                        return false;
                    }

                    if (!ImagesAllowed(ImagesOf(allowed), message.Images, out reason))
                    {
                        return false;
                    }

                    break;
                case PluginModelToolCall call when source is PluginModelToolCall original:
                    if (!string.Equals(call.CallId, original.CallId, StringComparison.Ordinal)
                        || !string.Equals(call.Name, original.Name, StringComparison.Ordinal)
                        || !string.Equals(call.Arguments, original.Arguments, StringComparison.Ordinal))
                    {
                        reason = "it changed a tool call.";
                        return false;
                    }

                    calls.Add(call.CallId);
                    callIndex[call.CallId] = index;
                    break;
                case PluginModelToolResult result when source is PluginModelToolResult original:
                    if (!string.Equals(result.CallId, original.CallId, StringComparison.Ordinal)
                        || !string.Equals(result.Name, original.Name, StringComparison.Ordinal)
                        || result.Success != original.Success)
                    {
                        reason = "it changed a tool result.";
                        return false;
                    }

                    if (!ImagesAllowed(ImagesOf(allowed), result.Images, out reason))
                    {
                        return false;
                    }

                    results.Add(result.CallId);
                    if (!callIndex.TryGetValue(result.CallId, out var callAt))
                    {
                        reason = "it split a tool call from its result.";
                        return false;
                    }

                    if (callAt > index)
                    {
                        reason = "it reordered a tool result ahead of its call.";
                        return false;
                    }

                    break;
                case PluginModelReasoning:
                    break;
                default:
                    reason = "it changed an item.";
                    return false;
            }
        }

        if (!SameSet(calls, results))
        {
            reason = "it split a tool call from its result.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool TryAcceptTransform(
        IReadOnlyList<PluginModelItem> current,
        IReadOnlyList<PluginModelItem> next,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        if (next.Count != current.Count)
        {
            reason = "it changed the model request structure.";
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            var source = current[index];
            var item = next[index];
            if (!string.Equals(source.Id, item.Id, StringComparison.Ordinal) || !SameKind(source, item))
            {
                reason = "it changed the model request structure.";
                return false;
            }

            switch (item)
            {
                case PluginModelMessage message when source is PluginModelMessage original:
                    if (message.Role != original.Role)
                    {
                        reason = "it changed a message role.";
                        return false;
                    }

                    if (message.Role == ChatRole.System
                        && (!string.Equals(message.Text, original.Text, StringComparison.Ordinal)
                            || !SameImages(original.Images, message.Images)))
                    {
                        reason = "it changed a system message.";
                        return false;
                    }

                    if (!ImagesAllowed(original.Images, message.Images, out reason))
                    {
                        return false;
                    }

                    break;
                case PluginModelToolCall call when source is PluginModelToolCall original:
                    if (!string.Equals(call.CallId, original.CallId, StringComparison.Ordinal)
                        || !string.Equals(call.Name, original.Name, StringComparison.Ordinal)
                        || !string.Equals(call.Arguments, original.Arguments, StringComparison.Ordinal))
                    {
                        reason = "it changed a tool call.";
                        return false;
                    }

                    break;
                case PluginModelToolResult result when source is PluginModelToolResult original:
                    if (!string.Equals(result.CallId, original.CallId, StringComparison.Ordinal)
                        || !string.Equals(result.Name, original.Name, StringComparison.Ordinal)
                        || result.Success != original.Success)
                    {
                        reason = "it changed a tool result.";
                        return false;
                    }

                    if (!ImagesAllowed(original.Images, result.Images, out reason))
                    {
                        return false;
                    }

                    break;
                case PluginModelReasoning:
                    break;
                default:
                    reason = "it changed an item.";
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    public static IReadOnlyList<ChatItem> Apply(
        Projection origin,
        IReadOnlyList<PluginModelItem> items)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(items);
        var applied = new List<ChatItem>(items.Count);
        foreach (var item in items)
        {
            applied.Add(ApplyItem(origin.Origins[item.Id], origin.ById[item.Id], item));
        }

        return applied;
    }

    private static PluginModelItem ProjectItem(
        string id,
        ChatItem item,
        IReadOnlyDictionary<string, string> toolNames,
        IReadOnlyDictionary<int, string> mediaTypes) => item switch
    {
        ChatMessage message => new PluginModelMessage(
            id,
            message.Role,
            ImageMarkerText.Display(message.Text),
            ReadImages(message.Text, mediaTypes)),
        ToolCall call => new PluginModelToolCall(id, call.CallId, call.Name, call.Arguments),
        ToolResult result => new PluginModelToolResult(
            id,
            result.CallId,
            toolNames.TryGetValue(result.CallId, out var name) ? name : string.Empty,
            ImageMarkerText.Display(result.Text),
            result.Status == ToolResultStatus.Success,
            ReadImages(result.Text, mediaTypes)),
        ChatReasoningItem reasoning => new PluginModelReasoning(id, ReasoningText(reasoning)),
        _ => throw new NotSupportedException(
            $"Model hook transcript does not support {item.GetType().Name}.")
    };

    private static ChatItem ApplyItem(
        ChatItem origin,
        PluginModelItem projected,
        PluginModelItem final) => final switch
    {
        PluginModelMessage message when origin is ChatMessage chat && projected is PluginModelMessage source =>
            ApplyMessage(chat, source, message),
        PluginModelToolCall when origin is ToolCall call => call,
        PluginModelToolResult result when origin is ToolResult tool && projected is PluginModelToolResult source =>
            ApplyResult(tool, source, result),
        PluginModelReasoning reasoning when origin is ChatReasoningItem item && projected is PluginModelReasoning source =>
            ApplyReasoning(item, source, reasoning),
        _ => throw new InvalidOperationException("Model hook item does not match its origin.")
    };

    private static ChatItem ApplyMessage(
        ChatMessage origin,
        PluginModelMessage projected,
        PluginModelMessage final)
    {
        var text = ApplyText(origin.Text, projected.Text, projected.Images, final.Text, final.Images);
        return string.Equals(text, origin.Text, StringComparison.Ordinal)
            ? origin
            : new ChatMessage(origin.Role, text);
    }

    private static ChatItem ApplyResult(
        ToolResult origin,
        PluginModelToolResult projected,
        PluginModelToolResult final)
    {
        var text = ApplyText(origin.Text, projected.Text, projected.Images, final.Text, final.Images);
        return string.Equals(text, origin.Text, StringComparison.Ordinal)
            ? origin
            : new ToolResult(origin.CallId, text, origin.Status);
    }

    private static ChatItem ApplyReasoning(
        ChatReasoningItem origin,
        PluginModelReasoning projected,
        PluginModelReasoning final)
    {
        if (string.Equals(final.Text, projected.Text, StringComparison.Ordinal))
        {
            return origin;
        }

        return new ChatReasoningItem(
            new ReasoningContent([new ReasoningText(final.Text, ReasoningTextKind.Summary)]));
    }

    private static string ApplyText(
        string originalRaw,
        string projectedDisplay,
        IReadOnlyList<PluginModelImage> originalImages,
        string returnedDisplay,
        IReadOnlyList<PluginModelImage> returnedImages)
    {
        var kept = new HashSet<int>();
        foreach (var image in returnedImages)
        {
            kept.Add(image.Number);
        }

        var text = string.Equals(returnedDisplay, projectedDisplay, StringComparison.Ordinal)
            ? originalRaw
            : returnedDisplay;
        foreach (var image in originalImages)
        {
            if (kept.Contains(image.Number))
            {
                continue;
            }

            text = text.Replace(ImageMarkerText.Tag(image.Number), string.Empty, StringComparison.Ordinal);
            text = text.Replace(DisplayMarker(image.Number), string.Empty, StringComparison.Ordinal);
        }

        foreach (var image in originalImages)
        {
            if (!kept.Contains(image.Number))
            {
                continue;
            }

            var trusted = ImageMarkerText.Tag(image.Number);
            if (text.Contains(trusted, StringComparison.Ordinal))
            {
                continue;
            }

            var display = DisplayMarker(image.Number);
            if (text.Contains(display, StringComparison.Ordinal))
            {
                text = text.Replace(display, trusted, StringComparison.Ordinal);
            }
            else
            {
                text = text.Length == 0 ? trusted : text + "\n" + trusted;
            }
        }

        return text;
    }

    private static string DisplayMarker(int number) => $"[Image #{number}]";

    private static List<PluginModelImage> ReadImages(
        string text,
        IReadOnlyDictionary<int, string> mediaTypes)
    {
        var images = new List<PluginModelImage>();
        var seen = new HashSet<int>();
        foreach (Match match in ImageMarkerText.Matches(text))
        {
            if (!int.TryParse(match.Groups[1].Value, out var number)
                || !seen.Add(number)
                || !mediaTypes.TryGetValue(number, out var mediaType))
            {
                continue;
            }

            images.Add(new PluginModelImage(number, mediaType));
        }

        return images;
    }

    private static Dictionary<string, string> ToolNames(IReadOnlyList<ChatItem> items)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is ToolCall call)
            {
                names[call.CallId] = call.Name;
            }
        }

        return names;
    }

    private static string ReasoningText(ChatReasoningItem reasoning)
    {
        var parts = new List<string>();
        foreach (var segment in reasoning.Content.TextSegments)
        {
            if (segment.Text.Length > 0)
            {
                parts.Add(segment.Text);
            }
        }

        return string.Join("\n", parts);
    }

    private static Dictionary<string, PluginModelItem> Index(IReadOnlyList<PluginModelItem> items)
    {
        var byId = new Dictionary<string, PluginModelItem>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            byId[item.Id] = item;
        }

        return byId;
    }

    private static bool SameKind(PluginModelItem left, PluginModelItem right) =>
        left.GetType() == right.GetType();

    private static IReadOnlyList<PluginModelImage> ImagesOf(PluginModelItem item) => item switch
    {
        PluginModelMessage message => message.Images,
        PluginModelToolResult result => result.Images,
        _ => []
    };

    private static bool ImagesAllowed(
        IReadOnlyList<PluginModelImage> allowed,
        IReadOnlyList<PluginModelImage> returned,
        out string reason)
    {
        var known = new Dictionary<int, string>();
        foreach (var image in allowed)
        {
            known[image.Number] = image.MediaType;
        }

        var seen = new HashSet<int>();
        foreach (var image in returned)
        {
            if (!known.TryGetValue(image.Number, out var mediaType)
                || !string.Equals(mediaType, image.MediaType, StringComparison.Ordinal)
                || !seen.Add(image.Number))
            {
                reason = "it added an image.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool SameImages(
        IReadOnlyList<PluginModelImage> left,
        IReadOnlyList<PluginModelImage> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Number != right[index].Number
                || !string.Equals(left[index].MediaType, right[index].MediaType, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameSet(List<string> calls, List<string> results)
    {
        if (calls.Count != results.Count)
        {
            return false;
        }

        var remaining = new HashSet<string>(calls, StringComparer.Ordinal);
        foreach (var callId in results)
        {
            if (!remaining.Remove(callId))
            {
                return false;
            }
        }

        return remaining.Count == 0;
    }

    internal sealed class Projection(
        IReadOnlyList<PluginModelItem> items,
        Dictionary<string, ChatItem> origins,
        Dictionary<string, PluginModelItem> byId,
        string? liveSystemId)
    {
        public IReadOnlyList<PluginModelItem> Items { get; } = items;

        public Dictionary<string, ChatItem> Origins { get; } = origins;

        public Dictionary<string, PluginModelItem> ById { get; } = byId;

        public string? LiveSystemId { get; } = liveSystemId;
    }
}
