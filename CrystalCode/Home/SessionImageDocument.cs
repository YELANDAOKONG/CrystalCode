namespace CrystalCode.Home;

/// <summary>One persisted image referenced by a transcript marker.</summary>
public sealed class SessionImageDocument
{
    public int Number { get; set; }

    public string? MimeType { get; set; }

    public byte[]? Data { get; set; }

    public string? Uri { get; set; }
}
