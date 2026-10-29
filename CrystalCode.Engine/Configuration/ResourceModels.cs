namespace CrystalCode.Engine.Configuration;

/// <summary>
/// The image, audio, and video resource description models together.
/// </summary>
public sealed record ResourceModels
{
    public static ResourceModels Off { get; } = new(ResourceModelSettings.Off, ResourceModelSettings.Off, ResourceModelSettings.Off);

    public ResourceModels(ResourceModelSettings image, ResourceModelSettings audio, ResourceModelSettings video)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(video);
        Image = image;
        Audio = audio;
        Video = video;
    }

    public ResourceModelSettings Image { get; }

    public ResourceModelSettings Audio { get; }

    public ResourceModelSettings Video { get; }

    public ResourceModelSettings For(ResourceKind kind) => kind switch
    {
        ResourceKind.Image => Image,
        ResourceKind.Audio => Audio,
        ResourceKind.Video => Video,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.")
    };

    public ResourceModels With(ResourceKind kind, ResourceModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return kind switch
        {
            ResourceKind.Image => new(model, Audio, Video),
            ResourceKind.Audio => new(Image, model, Video),
            ResourceKind.Video => new(Image, Audio, model),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown resource kind.")
        };
    }
}
