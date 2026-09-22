namespace CrystalCode.Sessions;

/// <summary>One immutable image attached to a session transcript.</summary>
public sealed record ImageAttachment
{
    private readonly byte[]? _data;

    public ImageAttachment(int number, string mimeType, ReadOnlyMemory<byte> data)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                "Image number must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (data.IsEmpty)
        {
            throw new ArgumentException("Image data cannot be empty.", nameof(data));
        }

        if (data.Length > ImageFile.MaximumBytes)
        {
            throw new ArgumentException(
                "Image data exceeds the 20 MiB host limit.",
                nameof(data));
        }

        Number = number;
        MimeType = mimeType;
        _data = data.ToArray();
    }

    public ImageAttachment(int number, string mimeType, Uri uri)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                "Image number must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("Image URI must be absolute.", nameof(uri));
        }

        if (uri.Scheme is not ("http" or "https")
            || uri.AbsoluteUri.Length > 8192)
        {
            throw new ArgumentException(
                "Image URI must use HTTP(S) and be at most 8192 characters.",
                nameof(uri));
        }

        Number = number;
        MimeType = mimeType;
        Uri = uri;
    }

    public int Number { get; }

    public string MimeType { get; }

    public ReadOnlyMemory<byte>? Data => _data?.ToArray();

    public Uri? Uri { get; }

    public string Marker => $"[Image #{Number}]";

    public bool Equals(ImageAttachment? other) =>
        other is not null
        && Number == other.Number
        && string.Equals(MimeType, other.MimeType, StringComparison.Ordinal)
        && Equals(Uri, other.Uri)
        && ((_data is null && other._data is null)
            || (_data is not null
                && other._data is not null
                && _data.AsSpan().SequenceEqual(other._data)));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Number);
        hash.Add(MimeType, StringComparer.Ordinal);
        hash.Add(Uri);
        foreach (var value in _data ?? [])
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
