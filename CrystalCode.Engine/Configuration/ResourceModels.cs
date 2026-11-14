namespace CrystalCode.Engine.Configuration;

/// <summary>
/// The image, audio, video, and document resource description models together.
/// </summary>
public sealed record ResourceModels
{
    public static ResourceModels Off { get; } = new(ResourceModelSettings.Off, ResourceModelSettings.Off, ResourceModelSettings.Off);

    public ResourceModels(ResourceModelSettings image, ResourceModelSettings audio, ResourceModelSettings video)
        : this(image, audio, video, ResourceModelSettings.Off)
    {
    }

    public ResourceModels(
        ResourceModelSettings image,
        ResourceModelSettings audio,
        ResourceModelSettings video,
        ResourceModelSettings document)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(document);
        Image = image;
        Audio = audio;
        Video = video;
        Document = document;
    }

    public ResourceModelSettings Image { get; }

    public ResourceModelSettings Audio { get; }

    public ResourceModelSettings Video { get; }

    public ResourceModelSettings Document { get; }

    public ResourceModelSettings For(ResourceKind kind) => kind switch
    {
        ResourceKind.Image => Image,
        ResourceKind.Audio => Audio,
        ResourceKind.Video => Video,
        ResourceKind.Document => Document,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.")
    };

    public ResourceModels With(ResourceKind kind, ResourceModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return kind switch
        {
            ResourceKind.Image => new(model, Audio, Video, Document),
            ResourceKind.Audio => new(Image, model, Video, Document),
            ResourceKind.Video => new(Image, Audio, model, Document),
            ResourceKind.Document => new(Image, Audio, Video, model),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.")
        };
    }
}
