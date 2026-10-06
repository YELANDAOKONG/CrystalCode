using System.Globalization;
using System.Text.RegularExpressions;

using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

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
        var known = new Dictionary<int, string>();
        for (var index = 0; index < items.Count; index++)
        {
            var id = index.ToString(CultureInfo.InvariantCulture);
            var item = ProjectItem(id, items[index], names, mediaTypes);
            projected.Add(item);
            origins.Add(id, items[index]);
            byId.Add(id, item);
            foreach (var image in ImagesOf(item))
            {
                known[image.Number] = image.MediaType;
            }
        }

        return new Projection(projected, origins, byId, known);
    }

    /// <summary>
    /// Accepts any replacement the host can represent. A raw hook may reorder,
    /// drop, add, and rewrite items of any kind. It cannot repeat an id,
    /// return empty reasoning text, or name an image that is not attached to
    /// the session. It cannot add an image to a request that does not carry
    /// images.
    /// </summary>
    public static bool TryAcceptRaw(
        Projection origin,
        IReadOnlyList<PluginModelItem> next,
        bool acceptsImages,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(next);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in next)
        {
            if (!seen.Add(item.Id))
            {
                reason = "it repeated an item.";
                return false;
            }

            origin.ById.TryGetValue(item.Id, out var source);
            switch (item)
            {
                case PluginModelMessage message:
                    if (!RawImagesAllowed(origin, source, message.Images, acceptsImages, out reason))
                    {
                        return false;
                    }

                    break;
                case PluginModelToolResult result:
                    if (!RawImagesAllowed(origin, source, result.Images, acceptsImages, out reason))
                    {
                        return false;
                    }

                    break;
                case PluginModelReasoning reasoning:
                    if (reasoning.Text.Length == 0
                        && !(source is PluginModelReasoning { Text.Length: 0 }))
                    {
                        reason = "it returned empty reasoning text.";
                        return false;
                    }

                    break;
                case PluginModelToolCall:
                    break;
                default:
                    reason = "it returned an unknown item.";
                    return false;
            }
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
                case PluginModelReasoning reasoning when source is PluginModelReasoning originalReasoning:
                    if (reasoning.Text.Length == 0 && originalReasoning.Text.Length != 0)
                    {
                        reason = "it returned empty reasoning text.";
                        return false;
                    }

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
            // An item that keeps the id and kind of a projected item rewrites
            // that item. Anything else is new and is built from the plugin value.
            if (origin.Origins.TryGetValue(item.Id, out var chat)
                && SameKind(origin.ById[item.Id], item))
            {
                applied.Add(ApplyItem(chat, origin.ById[item.Id], item));
                continue;
            }

            applied.Add(CreateItem(item));
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
        PluginModelToolCall call when origin is ToolCall source => ApplyCall(source, call),
        PluginModelToolResult result when origin is ToolResult tool && projected is PluginModelToolResult source =>
            ApplyResult(tool, source, result),
        PluginModelReasoning reasoning when origin is ChatReasoningItem item && projected is PluginModelReasoning source =>
            ApplyReasoning(item, source, reasoning),
        _ => throw new InvalidOperationException("Model hook item does not match its origin.")
    };

    private static ChatItem CreateItem(PluginModelItem item) => item switch
    {
        PluginModelMessage message => new ChatMessage(
            message.Role,
            Compose(message.Text, [], message.Images)),
        PluginModelToolCall call => new ToolCall(call.CallId, call.Name, call.Arguments),
        PluginModelToolResult result => new ToolResult(
            result.CallId,
            Compose(result.Text, [], result.Images),
            result.Success ? ToolResultStatus.Success : ToolResultStatus.Failure),
        PluginModelReasoning reasoning => new ChatReasoningItem(
            new ReasoningContent([new ReasoningText(reasoning.Text, ReasoningTextKind.Summary)])),
        _ => throw new InvalidOperationException("Model hook returned an item the host cannot build.")
    };

    private static ChatItem ApplyMessage(
        ChatMessage origin,
        PluginModelMessage projected,
        PluginModelMessage final)
    {
        var text = ApplyText(origin.Text, projected.Text, projected.Images, final.Text, final.Images);
        return final.Role == origin.Role && string.Equals(text, origin.Text, StringComparison.Ordinal)
            ? origin
            : new ChatMessage(final.Role, text);
    }

    private static ChatItem ApplyCall(ToolCall origin, PluginModelToolCall final) =>
        string.Equals(final.CallId, origin.CallId, StringComparison.Ordinal)
        && string.Equals(final.Name, origin.Name, StringComparison.Ordinal)
        && string.Equals(final.Arguments, origin.Arguments, StringComparison.Ordinal)
            ? origin
            : new ToolCall(final.CallId, final.Name, final.Arguments);

    private static ChatItem ApplyResult(
        ToolResult origin,
        PluginModelToolResult projected,
        PluginModelToolResult final)
    {
        var text = ApplyText(origin.Text, projected.Text, projected.Images, final.Text, final.Images);
        var status = final.Success == projected.Success
            ? origin.Status
            : final.Success ? ToolResultStatus.Success : ToolResultStatus.Failure;
        return string.Equals(final.CallId, origin.CallId, StringComparison.Ordinal)
            && string.Equals(text, origin.Text, StringComparison.Ordinal)
            && status == origin.Status
                ? origin
                : new ToolResult(final.CallId, text, status);
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
        var text = string.Equals(returnedDisplay, projectedDisplay, StringComparison.Ordinal)
            ? originalRaw
            : returnedDisplay;
        return Compose(text, originalImages, returnedImages);
    }

    /// <summary>
    /// Makes the text match its image list. A marker for an image that is no
    /// longer listed is removed. A listed image gets the trusted marker, which
    /// is the only spelling that references an attachment.
    /// </summary>
    private static string Compose(
        string text,
        IReadOnlyList<PluginModelImage> originalImages,
        IReadOnlyList<PluginModelImage> returnedImages)
    {
        var kept = new HashSet<int>();
        foreach (var image in returnedImages)
        {
            kept.Add(image.Number);
        }

        foreach (var image in originalImages)
        {
            if (kept.Contains(image.Number))
            {
                continue;
            }

            text = text.Replace(ImageMarkerText.Tag(image.Number), string.Empty, StringComparison.Ordinal);
            text = text.Replace(DisplayMarker(image.Number), string.Empty, StringComparison.Ordinal);
        }

        foreach (var image in returnedImages)
        {
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

    private static bool SameKind(PluginModelItem left, PluginModelItem right) =>
        left.GetType() == right.GetType();

    private static IReadOnlyList<PluginModelImage> ImagesOf(PluginModelItem? item) => item switch
    {
        PluginModelMessage message => message.Images,
        PluginModelToolResult result => result.Images,
        _ => []
    };

    /// <summary>
    /// A raw hook may keep an image its item already carried. It may also name
    /// any other attachment already present in the request, with the same media
    /// type. It cannot name one the session does not hold.
    /// </summary>
    private static bool RawImagesAllowed(
        Projection origin,
        PluginModelItem? source,
        IReadOnlyList<PluginModelImage> returned,
        bool acceptsImages,
        out string reason)
    {
        var onItem = new HashSet<int>();
        foreach (var image in ImagesOf(source))
        {
            onItem.Add(image.Number);
        }

        var seen = new HashSet<int>();
        foreach (var image in returned)
        {
            if (!origin.KnownImages.TryGetValue(image.Number, out var mediaType)
                || !string.Equals(mediaType, image.MediaType, StringComparison.Ordinal)
                || !seen.Add(image.Number))
            {
                reason = "it added an image.";
                return false;
            }

            if (!acceptsImages && !onItem.Contains(image.Number))
            {
                reason = "it added an image to a request that cannot carry images.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

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

    internal sealed class Projection(
        IReadOnlyList<PluginModelItem> items,
        Dictionary<string, ChatItem> origins,
        Dictionary<string, PluginModelItem> byId,
        IReadOnlyDictionary<int, string> knownImages)
    {
        public IReadOnlyList<PluginModelItem> Items { get; } = items;

        public Dictionary<string, ChatItem> Origins { get; } = origins;

        public Dictionary<string, PluginModelItem> ById { get; } = byId;

        /// <summary>Every attachment the request already references, by number.</summary>
        public IReadOnlyDictionary<int, string> KnownImages { get; } = knownImages;
    }
}
