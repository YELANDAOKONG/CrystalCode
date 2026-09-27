using System.Text.RegularExpressions;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Display.Composer;

namespace CrystalCode.Sessions;

internal static partial class ImageMarkerText
{
    public static string Tag(int number) =>
        $"{ComposerBuffer.ImageMarkerPrefix}[Image #{number}]";

    public static MatchCollection Matches(string text) => TrustedMarker().Matches(text);

    public static string Display(string text) =>
        TrustedMarker().Replace(text, static match => match.Value[1..]);

    public static IReadOnlyList<ChatItem> ForTextModel(IReadOnlyList<ChatItem> items) =>
    [
        .. items.Select(item => item switch
        {
            ChatMessage message => new ChatMessage(message.Role, Display(message.Text)),
            ToolResult result => new ToolResult(
                result.CallId,
                Display(result.Text),
                result.Status),
            _ => item
        })
    ];

    public static List<ChatItem> TagLegacy(
        IReadOnlyList<ChatItem> items,
        IReadOnlySet<int> imageNumbers)
    {
        return
        [
            .. items.Select(item => item switch
            {
                ChatMessage message when message.Role == ChatRole.User => new ChatMessage(
                    message.Role,
                    TagLegacyText(message.Text, imageNumbers)),
                ToolResult result => new ToolResult(
                    result.CallId,
                    TagLegacyText(result.Text, imageNumbers),
                    result.Status),
                _ => item
            })
        ];
    }

    private static string TagLegacyText(string text, IReadOnlySet<int> imageNumbers) =>
        ImageMarker().Replace(text, match =>
        {
            if (match.Index > 0
                && text[match.Index - 1] == ComposerBuffer.ImageMarkerPrefix)
            {
                return match.Value;
            }

            return int.TryParse(match.Groups[1].Value, out var number)
                && imageNumbers.Contains(number)
                ? ComposerBuffer.ImageMarkerPrefix + match.Value
                : match.Value;
        });

    [GeneratedRegex(@"\u2063\[Image #(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex TrustedMarker();

    [GeneratedRegex(@"\[Image #(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ImageMarker();
}
