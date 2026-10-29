using System.Globalization;
using System.Text.RegularExpressions;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Chooses which session resources a model request carries. Only images
/// exist today. An omitted resource is never deleted: the transcript and the
/// session keep it, and the request shows only its marker text. Omission is
/// one-way for the life of the trimmer, so a trimmed resource does not come
/// back when new ones arrive and the request prefix changes only when a trim
/// triggers.
/// </summary>
public sealed class ResourceTrimmer
{
    private readonly HashSet<int> _omitted = [];

    /// <summary>
    /// Trims by the automatic limits when they are enabled, then returns the
    /// images the request may carry. Earlier omissions always apply.
    /// </summary>
    public IDictionary<int, ImageAttachment> Apply(
        IReadOnlyList<ChatItem> transcript,
        IDictionary<int, ImageAttachment> images,
        ResourceTrimSettings settings)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Enabled)
        {
            TrimAutomatically(Referenced(transcript, images, settings.IncludeUserResources), settings);
        }

        var visible = new Dictionary<int, ImageAttachment>(images);
        foreach (var number in _omitted)
        {
            visible.Remove(number);
        }

        return visible;
    }

    /// <summary>
    /// Leaves all but the newest <paramref name="keep"/> trimmable images out
    /// of later requests, whether or not automatic trimming is enabled.
    /// </summary>
    public ResourceTrimResult Trim(
        IReadOnlyList<ChatItem> transcript,
        IDictionary<int, ImageAttachment> images,
        int keep,
        bool includeUserResources)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);
        var trimmable = Referenced(transcript, images, includeUserResources)
            .Where(static entry => entry.Trimmable)
            .ToList();
        return Omit(trimmable.Take(Math.Max(0, trimmable.Count - keep)));
    }

    public void Reset() => _omitted.Clear();

    private void TrimAutomatically(List<Reference> referenced, ResourceTrimSettings settings)
    {
        var trimmable = referenced.Where(static entry => entry.Trimmable).ToList();
        if (trimmable.Count > settings.KeepRecent + settings.Slack)
        {
            Omit(trimmable.Take(trimmable.Count - settings.KeepRecent));
            trimmable = [.. trimmable.Where(entry => !_omitted.Contains(entry.Image.Number))];
        }

        if (settings.MaxBytes == 0)
        {
            return;
        }

        var total = referenced
            .Where(entry => !_omitted.Contains(entry.Image.Number))
            .Sum(static entry => (long)entry.Image.ByteCount);
        // The newest trimmable image always stays, even when it alone exceeds the budget.
        foreach (var entry in trimmable.Take(Math.Max(0, trimmable.Count - 1)))
        {
            if (total <= settings.MaxBytes)
            {
                return;
            }

            _omitted.Add(entry.Image.Number);
            total -= entry.Image.ByteCount;
        }
    }

    private ResourceTrimResult Omit(IEnumerable<Reference> entries)
    {
        var count = 0;
        long bytes = 0;
        foreach (var entry in entries)
        {
            if (_omitted.Add(entry.Image.Number))
            {
                count++;
                bytes += entry.Image.ByteCount;
            }
        }

        return new ResourceTrimResult(count, bytes);
    }

    /// <summary>
    /// Images the transcript still shows, oldest first by first mention. An
    /// image already omitted is left out.
    /// </summary>
    private List<Reference> Referenced(
        IReadOnlyList<ChatItem> transcript,
        IDictionary<int, ImageAttachment> images,
        bool includeUserResources)
    {
        var references = new List<Reference>();
        var seen = new HashSet<int>();
        foreach (var item in transcript)
        {
            var (text, trimmable) = item switch
            {
                ToolResult result => (result.Text, true),
                ChatMessage { Role: var role } message when role == ChatRole.User =>
                    (message.Text, includeUserResources),
                _ => (null, false)
            };
            if (text is null)
            {
                continue;
            }

            foreach (Match match in ImageMarkerText.Matches(text))
            {
                if (int.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var number)
                    && images.TryGetValue(number, out var image)
                    && seen.Add(number)
                    && !_omitted.Contains(number))
                {
                    references.Add(new Reference(image, trimmable));
                }
            }
        }

        return references;
    }

    private readonly record struct Reference(ImageAttachment Image, bool Trimmable);
}
