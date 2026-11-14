namespace CrystalCode.Engine.Configuration;

/// <summary>Names and catalog checks for each <see cref="ResourceKind"/>.</summary>
public static class ResourceKinds
{
    public static IReadOnlyList<ResourceKind> All { get; } = [ResourceKind.Image, ResourceKind.Audio, ResourceKind.Video, ResourceKind.Document];

    /// <summary>
    /// Whether a tool uses that model. Audio, video, and document description are not
    /// connected yet; their settings are stored and validated only.
    /// </summary>
    public static bool IsConnected(ResourceKind kind) => kind == ResourceKind.Image;

    /// <summary>The lowercase word used in commands, ids, and config fields.</summary>
    public static string Word(ResourceKind kind) => kind switch
    {
        ResourceKind.Image => "image",
        ResourceKind.Audio => "audio",
        ResourceKind.Video => "video",
        ResourceKind.Document => "document",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.")
    };

    public static string Title(ResourceKind kind) => kind switch
    {
        ResourceKind.Image => "Image",
        ResourceKind.Audio => "Audio",
        ResourceKind.Video => "Video",
        ResourceKind.Document => "Document",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.")
    };

    public static bool TryParse(string? text, out ResourceKind kind)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(text, Word(candidate), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = ResourceKind.Image;
        return false;
    }

    /// <summary>Whether the catalog model accepts the input the kind describes.</summary>
    public static bool Accepts(ModelSettings model, ResourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(model);
        return kind switch
        {
            ResourceKind.Image => model.ImageInput,
            ResourceKind.Audio => model.AudioInput,
            ResourceKind.Video => model.VideoInput,
            ResourceKind.Document => model.FileInput,
            _ => false
        };
    }

    /// <summary>The <c>providers.json</c> flag the model must declare.</summary>
    public static string InputFlag(ResourceKind kind) =>
        kind == ResourceKind.Document ? "fileInput" : Word(kind) + "Input";
}
