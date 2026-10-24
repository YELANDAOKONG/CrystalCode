namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// Changes an existing file in place and, when <paramref name="MovePath"/> is
/// set, moves the result to that path.
/// </summary>
internal sealed record PatchUpdate(
    string Path,
    string? MovePath,
    IReadOnlyList<PatchChunk> Chunks) : PatchOperation(Path)
{
    public override IEnumerable<string> Paths => MovePath is null ? [Path] : [Path, MovePath];
}
