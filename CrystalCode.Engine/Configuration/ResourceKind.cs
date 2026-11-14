namespace CrystalCode.Engine.Configuration;

/// <summary>
/// The input a resource description model describes for a session model that
/// cannot read it. Only <see cref="Image"/> is connected; audio, video, and documents are
/// configuration only.
/// </summary>
public enum ResourceKind
{
    Image,
    Audio,
    Video,
    Document
}
