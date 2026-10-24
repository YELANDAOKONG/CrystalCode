namespace CrystalCode.Engine.Modes;

internal sealed record ModeManifest
{
    public string? Description { get; init; }

    public bool Enabled { get; init; }

    public string[]? Tools { get; init; }
}
