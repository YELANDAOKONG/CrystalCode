namespace CrystalCode.Plugins.Hooks;

/// <summary>One image already attached to a model item. The host does not include the bytes.</summary>
public sealed record PluginModelImage
{
    public PluginModelImage(int number, string mediaType)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                "Image number must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        Number = number;
        MediaType = mediaType.Trim();
    }

    public int Number { get; }

    public string MediaType { get; }

    public override string ToString() => nameof(PluginModelImage);
}
