namespace CrystalCode.Engine.Configuration;

/// <summary>
/// The input a resource description model describes for a session model that
/// cannot read it. Only <see cref="Image"/> is connected; audio and video are
/// configuration only.
/// </summary>
public enum ResourceKind
{
    Image,
    Audio,
    Video
}
