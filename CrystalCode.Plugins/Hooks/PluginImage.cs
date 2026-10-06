namespace CrystalCode.Plugins.Hooks;

/// <summary>One inline image a tool-result hook may return.</summary>
public sealed record PluginImage
{
    public PluginImage(string mediaType, ReadOnlyMemory<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        MediaType = mediaType.Trim();
        Data = data;
    }

    public string MediaType { get; }

    public ReadOnlyMemory<byte> Data { get; }

    public override string ToString() => nameof(PluginImage);
}
