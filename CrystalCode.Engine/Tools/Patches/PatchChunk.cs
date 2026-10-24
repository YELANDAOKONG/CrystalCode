namespace CrystalCode.Engine.Tools.Patches;

/// <summary>
/// One group of changes inside an Update File section. Every context line
/// must be found, in order, before the old lines are looked for.
/// </summary>
internal sealed record PatchChunk(
    IReadOnlyList<string> Contexts,
    IReadOnlyList<string> OldLines,
    IReadOnlyList<string> NewLines,
    bool AtEndOfFile);
